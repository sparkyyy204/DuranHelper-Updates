#include "binder.h"
#include "gui.h"
#include <fstream>
#include <iostream>
#include <shlobj.h>
#include <algorithm>
#include <cctype>
#include <thread>
#include <chrono>

extern HMODULE g_hModule; // From main.cpp

// Helper: Get Settings/Profiles path from Desktop/test/ for development
#include <Knownfolders.h>
#include <combaseapi.h>

std::string GetAppDataPath() {
    PWSTR pathStr = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Documents, 0, NULL, &pathStr))) {
        int size_needed = WideCharToMultiByte(CP_ACP, 0, pathStr, -1, NULL, 0, NULL, NULL);
        std::string strTo(size_needed, 0);
        WideCharToMultiByte(CP_ACP, 0, pathStr, -1, &strTo[0], size_needed, NULL, NULL);
        CoTaskMemFree(pathStr);
        if (!strTo.empty() && strTo.back() == '\0') {
            strTo.pop_back();
        }
        return strTo + "\\DURAN HELPER\\";
    }

    // Fallback to older API
    char path[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathA(NULL, CSIDL_PERSONAL, NULL, 0, path))) {
        return std::string(path) + "\\DURAN HELPER\\";
    }
    return "";
}

// Map AHK Key String to Virtual-Key code
int BinderManager::StringToVK(const std::string& keyStr) {
    if (keyStr.empty()) return 0;
    
    std::string k = keyStr;
    std::transform(k.begin(), k.end(), k.begin(), ::tolower);

    if (k == "space") return VK_SPACE;
    if (k == "enter") return VK_RETURN;
    if (k == "escape" || k == "esc") return VK_ESCAPE;
    if (k == "backspace") return VK_BACK;
    if (k == "tab") return VK_TAB;
    if (k == "up") return VK_UP;
    if (k == "down") return VK_DOWN;
    if (k == "left") return VK_LEFT;
    if (k == "right") return VK_RIGHT;
    
    if (k == "lbutton") return VK_LBUTTON;
    if (k == "rbutton") return VK_RBUTTON;
    if (k == "mbutton") return VK_MBUTTON;
    if (k == "xbutton1") return VK_XBUTTON1;
    if (k == "xbutton2") return VK_XBUTTON2;

    if (k == "delete") return VK_DELETE;
    if (k == "insert") return VK_INSERT;
    if (k == "home") return VK_HOME;
    if (k == "end") return VK_END;
    if (k == "pgup" || k == "prior") return VK_PRIOR;
    if (k == "pgdn" || k == "next") return VK_NEXT;
    if (k == "capslock" || k == "capital") return VK_CAPITAL;
    if (k == "scrolllock") return VK_SCROLL;
    if (k == "numlock") return VK_NUMLOCK;
    if (k == "printscreen" || k == "snapshot") return VK_SNAPSHOT;
    if (k == "pause") return VK_PAUSE;

    if (k == "lshift") return VK_LSHIFT;
    if (k == "rshift") return VK_RSHIFT;
    if (k == "shift") return VK_SHIFT;
    if (k == "lctrl") return VK_LCONTROL;
    if (k == "rctrl") return VK_RCONTROL;
    if (k == "ctrl" || k == "control") return VK_CONTROL;
    if (k == "lalt") return VK_LMENU;
    if (k == "ralt") return VK_RMENU;
    if (k == "alt") return VK_MENU;

    if (k == "numpad0") return VK_NUMPAD0;
    if (k == "numpad1") return VK_NUMPAD1;
    if (k == "numpad2") return VK_NUMPAD2;
    if (k == "numpad3") return VK_NUMPAD3;
    if (k == "numpad4") return VK_NUMPAD4;
    if (k == "numpad5") return VK_NUMPAD5;
    if (k == "numpad6") return VK_NUMPAD6;
    if (k == "numpad7") return VK_NUMPAD7;
    if (k == "numpad8") return VK_NUMPAD8;
    if (k == "numpad9") return VK_NUMPAD9;
    
    if (k == "numpadmult") return VK_MULTIPLY;
    if (k == "numpaddiv") return VK_DIVIDE;
    if (k == "numpadadd") return VK_ADD;
    if (k == "numpadsub") return VK_SUBTRACT;
    if (k == "numpaddot") return VK_DECIMAL;
    if (k == "numpadenter") return VK_RETURN;

    if (k.length() == 1) { // generic single character e.g. "a", "1"
        char c = k[0];
        if (c >= 'a' && c <= 'z') return c - 'a' + 'A';
        if (c >= '0' && c <= '9') return c;
        if (c == '[') return 0xDB; // VK_OEM_4
        if (c == ']') return 0xDD; // VK_OEM_6
        if (c == ';') return 0xBA; // VK_OEM_1
        if (c == '\'') return 0xDE; // VK_OEM_7
        if (c == ',') return 0xBC; // VK_OEM_COMMA
        if (c == '.') return 0xBE; // VK_OEM_PERIOD
        if (c == '/') return 0xBF; // VK_OEM_2
        if (c == '\\') return 0xDC; // VK_OEM_5
        if (c == '`') return 0xC0; // VK_OEM_3
        if (c == '-') return 0xBD; // VK_OEM_MINUS
        if (c == '=') return 0xBB; // VK_OEM_PLUS
    }

    // F1 to F24
    if (k.length() > 1 && k[0] == 'f') {
        int num = std::atoi(k.c_str() + 1);
        if (num >= 1 && num <= 24) return VK_F1 + (num - 1);
    }
    
    return 0; // Not mapped
}

void BinderManager::ParseAhkKey(BindItem& bind) {
    bind.vkCode = 0;
    bind.needsAlt = false;
    bind.needsCtrl = false;
    bind.needsShift = false;

    std::string key = bind.ahkKey;
    if (key.empty() || key.find("НЕТ") != std::string::npos || key.find("None") != std::string::npos) return;

    // Check for WPF/C# strings like "Alt + Q", "Ctrl + Shift + F9"
    std::string lowerKey = key;
    std::transform(lowerKey.begin(), lowerKey.end(), lowerKey.begin(), ::tolower);
    
    if (lowerKey.find("alt") != std::string::npos && lowerKey.find("alt") < lowerKey.find_last_of('+')) bind.needsAlt = true;
    if (lowerKey.find("ctrl") != std::string::npos && lowerKey.find("ctrl") < lowerKey.find_last_of('+')) bind.needsCtrl = true;
    if (lowerKey.find("shift") != std::string::npos && lowerKey.find("shift") < lowerKey.find_last_of('+')) bind.needsShift = true;

    // Remove "alt + ", "ctrl + ", "shift + ", and spaces
    std::string cleanKey = "";
    for (size_t i = 0; i < lowerKey.length(); ++i) {
        if (lowerKey.compare(i, 4, "alt ") == 0 || lowerKey.compare(i, 4, "alt+") == 0) { i += 3; continue; }
        if (lowerKey.compare(i, 5, "ctrl ") == 0 || lowerKey.compare(i, 5, "ctrl+") == 0) { i += 4; continue; }
        if (lowerKey.compare(i, 6, "shift ") == 0 || lowerKey.compare(i, 6, "shift+") == 0) { i += 5; continue; }
        if (lowerKey[i] != '+' && lowerKey[i] != ' ') {
            cleanKey += key[i]; // keep original case for StringToVK just in case
        }
    }

    // Now try to strip AHK prefixes (!, ^, +) from the cleaned key, just in case
    size_t i = 0;
    while (i < cleanKey.length()) {
        char c = cleanKey[i];
        if (c == '!') bind.needsAlt = true;
        else if (c == '^') bind.needsCtrl = true;
        else if (c == '+') bind.needsShift = true;
        else if (c == '~' || c == '*') { /* ignore */ }
        else break;
        i++;
    }

    std::string finalKey = cleanKey.substr(i);
    bind.vkCode = StringToVK(finalKey);
}

bool BindItem::Matches(int vkPressed) const {
    if (!active || vkCode == 0) return false;
    if (vkCode != vkPressed) return false;

    bool isAltPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
    bool isCtrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    bool isShiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    if (needsAlt != isAltPressed) return false;
    if (needsCtrl != isCtrlPressed) return false;
    if (needsShift != isShiftPressed) return false;

    return true;
}

std::string BindItem::KeyComboStr() const {
    if (vkCode == 0 && ahkKey.empty()) return "";
    // If ahkKey is already in "Alt + Q" format, return it uppercase
    if (!ahkKey.empty()) {
        std::string r = ahkKey;
        std::transform(r.begin(), r.end(), r.begin(), [](unsigned char c) { return std::toupper(c); });
        return r;
    }
    return "";
}

// Forward declaration from main.cpp
extern void ExecuteBindActions(const BindItem& bind);

void BinderManager::ExecuteBind(const BindItem& bind) {
    ExecuteBindActions(bind);
}

void BinderManager::ReloadBinds() {
    Binds.clear();
    std::string baseDir = GetAppDataPath();
    if (baseDir.empty()) return;

    try {
        // 1. Read Settings to find LastProfile
        std::ifstream settingsFile(baseDir + "Settings.json");
        if (!settingsFile.is_open()) return;
        
        json jSettings;
        settingsFile >> jSettings;
        settingsFile.close();

        std::string keyToggleStr = jSettings.value("KeyToggle", "F9");
        BindItem tempToggle;
        tempToggle.ahkKey = keyToggleStr;
        BinderManager::Get().ParseAhkKey(tempToggle);
        Gui::toggleKey = tempToggle.vkCode;
        if (Gui::toggleKey == 0) Gui::toggleKey = VK_F9;
        Gui::toggleNeedsAlt = tempToggle.needsAlt;
        Gui::toggleNeedsCtrl = tempToggle.needsCtrl;
        Gui::toggleNeedsShift = tempToggle.needsShift;
        std::string lastProfile = jSettings.value("LastProfile", "");
        if (lastProfile.empty()) return;

        // 2. Read Profiles.json
        std::ifstream profilesFile(baseDir + "Profiles.json");
        if (!profilesFile.is_open()) return;

        json jProfiles;
        profilesFile >> jProfiles;
        profilesFile.close();

        if (!jProfiles.contains(lastProfile)) return;
        
        std::string themeOverlay = jProfiles[lastProfile].value("OverlayTheme", "Default (Dark Blue)");
        if (themeOverlay.find("Black") != std::string::npos) Gui::currentTheme = 1;
        else if (themeOverlay.find("Grey") != std::string::npos || themeOverlay.find("Sport") != std::string::npos) Gui::currentTheme = 2;
        else Gui::currentTheme = 0;
        Gui::ApplyTheme();
        auto& profileObj = jProfiles[lastProfile];

        if (!profileObj.contains("Binds")) return;
        auto& bindsObj = profileObj["Binds"];

        // Load specific variables for this profile
        Variables.clear();
        if (profileObj.contains("Variables") && profileObj["Variables"].is_object()) {
            for (auto& el : profileObj["Variables"].items()) {
                if (el.value().is_string()) {
                    Variables[el.key()] = el.value().get<std::string>();
                }
            }
        }

        // Iterate through all binds in object map
        for (auto& el : bindsObj.items()) {
            auto& bData = el.value();
            
            BindItem b;
            b.id = bData.value("id", "");
            b.name = bData.value("name", "");
            b.ahkKey = (bData.contains("key") && bData["key"].is_string()) ? bData["key"].get<std::string>() : "";
            b.active = bData.value("active", true);
            b.Group = bData.value("group", "");
            b.isAuto = bData.value("isAuto", false);

            if (b.isAuto) {
                b.autoTrigger = b.ahkKey; // In auto mode, 'key' field is the trigger text
                b.ahkKey = ""; // Clear hotkey since it's not a hotkey bind
            }

            ParseAhkKey(b);

            if (bData.contains("steps") && bData["steps"].is_array()) {
                for (auto& sData : bData["steps"]) {
                    BindStep step;
                    step.action = sData.value("action", "");
                    step.value = sData.value("value", "");
                    step.isEnter = sData.value("isEnter", true);
                    b.steps.push_back(step);
                }
            }
            Binds.push_back(b);
        }
    } catch (const std::exception& e) {
        // Output debug string to help with JSON parse errors
        OutputDebugStringA(("JSON Parse Error: " + std::string(e.what()) + "\n").c_str());
    }
}

void Gui::LoadRadialConfig() {
    radialSectors.clear();
    radialSectorCount = 4;
    std::string baseDir = GetAppDataPath();
    if (baseDir.empty()) return;

    try {
        std::ifstream f(baseDir + "radial.json");
        if (!f.is_open()) return;
        json j;
        f >> j;
        f.close();

        Gui::radialEnabled = j.value("enabled", true);
        Gui::radialMode = j.value("mode", "Standard");
        
        Gui::radialSectors.clear();
        radialSectorCount = j.value("sectorCount", 4);
        if (j.contains("sectors") && j["sectors"].is_array()) {
            for (auto& s : j["sectors"]) {
                RadialSector sec;
                sec.bindId = s.value("bindId", "");
                sec.bindName = s.value("bindName", "");
                sec.icon = s.value("icon", "star");
                sec.requiresId = s.value("requiresId", false);
                radialSectors.push_back(sec);
            }
        }
        
        Gui::radialGroupCount = j.value("groupCount", 4);
        Gui::radialGroups.clear();
        if (j.contains("groups") && j["groups"].is_array()) {
            for (auto& g : j["groups"]) {
                RadialMenuGroup grp;
                grp.name = g.value("name", "");
                grp.sectorCount = g.value("sectorCount", 4);
                if (g.contains("sectors") && g["sectors"].is_array()) {
                    for (auto& s : g["sectors"]) {
                        RadialSector sec;
                        sec.bindId = s.value("bindId", "");
                        sec.bindName = s.value("bindName", "");
                        sec.icon = s.value("icon", "star");
                        sec.requiresId = s.value("requiresId", false);
                        grp.sectors.push_back(sec);
                    }
                }
                Gui::radialGroups.push_back(grp);
            }
        }
    } catch (...) {}
}

