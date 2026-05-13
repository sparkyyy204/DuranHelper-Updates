#include "gui.h"
#include "binder.h"
#include <fstream>
#include <vector>
#include <string>
#include <map>
#include <algorithm>
#include <ctime>
#include <functional>
#include <windows.h>
#include <thread>
#include <chrono>
#include <atomic>
#include "json.hpp"

extern std::atomic<bool> g_CancelQuote;
extern float g_ImGuiGlobalScale;

using json = nlohmann::ordered_json;

// Forward declare for Fine sequence from main.cpp
extern void StartFineSequence(const std::string& targetId, const std::string& articleMsg, bool doRevoke, bool doQuote, const std::string& quoteText);

// ===== Static Member Initialization =====
extern HWND g_hWnd;
bool Gui::show = false;
bool Gui::clearNextFrame = false;
float Gui::alpha = 0.0f;
int Gui::activeTab = 0;
std::string Gui::versionStr = "?.?.?";

ImFont* Gui::fontArialBlack24 = nullptr;
ImFont* Gui::fontSegoeBold12 = nullptr;
ImFont* Gui::fontSegoeBold14 = nullptr;
ImFont* Gui::fontSegoeBold20 = nullptr;
ImFont* Gui::fontSegoeBlack32 = nullptr;

std::vector<LawSection> Gui::lawSections;
int Gui::selectedLawSection = 0;
bool Gui::showLawDropdown = false;
bool Gui::resetLawsScroll = false;
char Gui::searchLaws[256] = "";
float Gui::lawScrollY = 0.0f;

std::vector<FineItem> Gui::fineItems;
char Gui::searchFines[256] = "";
char Gui::fineIdBuf[32] = "";
bool Gui::fineWithRevoke = false;
float Gui::fineScrollY = 0.0f;

int Gui::selectedBindGroup = -1;
char Gui::searchBinder[256] = "";
float Gui::binderScrollY = 0.0f;
bool Gui::showSettings = false;
float Gui::globalScale = 1.0f;
float Gui::settingsAlpha = 0.85f;
int Gui::toggleKey = VK_F9;
std::string Gui::toggleKeyStr = "F9";
bool Gui::toggleNeedsAlt = false;
bool Gui::toggleNeedsCtrl = false;
bool Gui::toggleNeedsShift = false;
int Gui::currentTheme = 0;

int Gui::binderDelay = 1000;
bool Gui::rememberTab = true;
bool Gui::searchCurrentSection = true;

bool Gui::quoteEnabled = true;
bool Gui::quoteExtended = false;
bool Gui::quoteChapter = false;
bool Gui::quoteFines = true;

std::string Gui::overlayErrorMsg = "";
ImU32 Gui::overlayErrorColor = IM_COL32(231,76,60,255);
float Gui::overlayErrorTimer = 0.0f;

std::vector<PlayerRecord> Gui::playerDb;

bool Gui::radialMenuOpen = false;
bool Gui::radialEnabled = true;
std::string Gui::radialMode = "Standard";
int Gui::radialSectorCount = 4;
std::vector<RadialSector> Gui::radialSectors;
int Gui::radialGroupCount = 4;
std::vector<RadialMenuGroup> Gui::radialGroups;
int Gui::radialSelectedGroup = -1;
int Gui::radialHoveredGroup = -1;
int Gui::radialHoveredSector = -1;
bool Gui::radialIdInputOpen = false;
char Gui::radialIdBuffer[32] = "";
int Gui::radialIdTargetSector = -1;
bool Gui::radialIdFocusRequest = false;
bool Gui::radialJustOpened = false;

extern void RunOnMainThread(std::function<void()> task);
extern void OpenChatWithText(const char* text);
extern void SendSAMPMessage(const char* msg);
extern std::string UTF8ToCP1251(const char* utf8);

void Gui::LoadSettings() {
    std::string path = GetAppDataPath() + "Settings.json";
    std::ifstream file(path);
    if (file.is_open()) {
        try {
            json j;
            file >> j;
            if (j.contains("KeyToggle")) {
                std::string k = j["KeyToggle"];
                toggleKeyStr = k;
                BindItem tempToggle;
                tempToggle.ahkKey = k;
                BinderManager::Get().ParseAhkKey(tempToggle);
                toggleKey = tempToggle.vkCode;
                if (toggleKey == 0) toggleKey = VK_F9; // fallback
                toggleNeedsAlt = tempToggle.needsAlt;
                toggleNeedsCtrl = tempToggle.needsCtrl;
                toggleNeedsShift = tempToggle.needsShift;
            }
            if (j.contains("ThemeOverlay")) {
                std::string t = j["ThemeOverlay"];
                if (t.find("Black") != std::string::npos) currentTheme = 1;
                else if (t.find("Grey") != std::string::npos || t.find("Sport") != std::string::npos) currentTheme = 2;
                else currentTheme = 0;
            }
            if (j.contains("BinderDelay")) binderDelay = j["BinderDelay"].get<int>();
            if (j.contains("OverlayAlpha")) settingsAlpha = j["OverlayAlpha"].get<float>();
            if (j.contains("RememberTab")) rememberTab = j["RememberTab"].get<bool>();

            if (j.contains("SearchCurrentSection")) searchCurrentSection = j["SearchCurrentSection"].get<bool>();
            if (j.contains("LastTab") && rememberTab) activeTab = j["LastTab"].get<int>();
            
            if (j.contains("QuoteEnabled")) quoteEnabled = j["QuoteEnabled"].get<bool>();
            if (j.contains("QuoteExtended")) quoteExtended = j["QuoteExtended"].get<bool>();
            if (j.contains("QuoteChapter")) quoteChapter = j["QuoteChapter"].get<bool>();
            if (j.contains("QuoteFines")) quoteFines = j["QuoteFines"].get<bool>();
            
            ApplyTheme();
        } catch(...) {}
    }
}

void Gui::SaveSettings() {
    std::string path = GetAppDataPath() + "Settings.json";
    json j;
    std::ifstream fileIn(path);
    if (fileIn.is_open()) { try { fileIn >> j; } catch(...) {} fileIn.close(); }
    
    j["KeyToggle"] = toggleKeyStr;
    if (currentTheme == 1) j["ThemeOverlay"] = "Black";
    else if (currentTheme == 2) j["ThemeOverlay"] = "Grey";
    else j["ThemeOverlay"] = "Default (Dark Blue)";
    j["BinderDelay"] = binderDelay;
    j["OverlayAlpha"] = settingsAlpha;
    j["RememberTab"] = rememberTab;

    j["SearchCurrentSection"] = searchCurrentSection;
    j["LastTab"] = activeTab;
    
    j["QuoteEnabled"] = quoteEnabled;
    j["QuoteExtended"] = quoteExtended;
    j["QuoteChapter"] = quoteChapter;
    j["QuoteFines"] = quoteFines;
    
    std::ofstream fileOut(path);
    if (fileOut.is_open()) fileOut << j.dump(4);
}

void Gui::ShowError(const std::string& msg, ImU32 color) {
    overlayErrorMsg = msg;
    overlayErrorColor = color;
    overlayErrorTimer = 4.0f;
}

// ===== Color Constants =====
ImU32 C_BG          = IM_COL32(10,13,18,234);
ImU32 C_HEADER      = IM_COL32(17,21,29,204);
ImU32 C_BOX         = IM_COL32(22,27,34,255);
ImU32 C_INPUT       = IM_COL32(8,10,15,255);
ImU32 C_BORDER      = IM_COL32(31,36,46,255);
ImU32 C_LINE        = IM_COL32(48,54,61,255);
ImU32 C_GOLD_L      = IM_COL32(243,211,153,255);
ImU32 C_GOLD        = IM_COL32(210,166,94,255);
ImU32 C_GOLD_DIM    = IM_COL32(210,166,94,128);
ImU32 C_GOLD_BG     = IM_COL32(210,166,94,38);
ImU32 C_GRAY        = IM_COL32(139,148,158,255);
ImU32 C_GRAY2       = IM_COL32(107,115,127,255);
ImU32 C_RED         = IM_COL32(231,76,60,255);
ImU32 C_RED_BG      = IM_COL32(231,76,60,38);
ImU32 C_GREEN       = IM_COL32(46,160,67,255);
ImU32 C_GREEN_L     = IM_COL32(60,206,85,255);
ImU32 C_WHITE       = IM_COL32(255,255,255,255);
ImU32 C_DARK        = IM_COL32(8,10,15,255);
ImU32 C_GRID        = IM_COL32(22,27,34,102);
ImU32 C_HOVER       = IM_COL32(31,36,46,180);

void Gui::ApplyTheme() {
    if (currentTheme == 1) { // Black
        C_BG = IM_COL32(8,8,8,240);
        C_HEADER = IM_COL32(12,12,12,255);
        C_BOX = IM_COL32(18,18,18,255);
        C_INPUT = IM_COL32(5,5,5,255);
        C_BORDER = IM_COL32(28,28,28,255);
        C_LINE = IM_COL32(38,38,38,255);
        C_GRID = IM_COL32(25,25,25,102);
        C_DARK = IM_COL32(0,0,0,255);
        C_HOVER = IM_COL32(28,28,28,180);
    } else if (currentTheme == 2) { // Grey
        C_BG = IM_COL32(24,24,24,240);
        C_HEADER = IM_COL32(30,30,30,255);
        C_BOX = IM_COL32(40,40,40,255);
        C_INPUT = IM_COL32(15,15,15,255);
        C_BORDER = IM_COL32(55,55,55,255);
        C_LINE = IM_COL32(70,70,70,255);
        C_GRID = IM_COL32(45,45,45,102);
        C_DARK = IM_COL32(15,15,15,255);
        C_HOVER = IM_COL32(50,50,50,180);
    } else { // Default (Dark Blue)
        C_BG = IM_COL32(10,13,18,234);
        C_HEADER = IM_COL32(17,21,29,204);
        C_BOX = IM_COL32(22,27,34,255);
        C_INPUT = IM_COL32(8,10,15,255);
        C_BORDER = IM_COL32(31,36,46,255);
        C_LINE = IM_COL32(48,54,61,255);
        C_GRID = IM_COL32(22,27,34,102);
        C_DARK = IM_COL32(8,10,15,255);
        C_HOVER = IM_COL32(31,36,46,180);
    }
    
    // Update ImGui style colors to match theme (only if ImGui is initialized)
    if (!ImGui::GetCurrentContext()) return;
    ImGuiStyle& style = ImGui::GetStyle();
    ImVec4* c = style.Colors;
    float r, g, b, a;
    // FrameBg РІР‚вЂќ use C_INPUT
    r = ((C_INPUT >> 0) & 0xFF) / 255.0f; g = ((C_INPUT >> 8) & 0xFF) / 255.0f;
    b = ((C_INPUT >> 16) & 0xFF) / 255.0f; a = ((C_INPUT >> 24) & 0xFF) / 255.0f;
    c[ImGuiCol_FrameBg] = ImVec4(r, g, b, a);
    // FrameBgHovered РІР‚вЂќ use C_HOVER
    r = ((C_HOVER >> 0) & 0xFF) / 255.0f; g = ((C_HOVER >> 8) & 0xFF) / 255.0f;
    b = ((C_HOVER >> 16) & 0xFF) / 255.0f; a = ((C_HOVER >> 24) & 0xFF) / 255.0f;
    c[ImGuiCol_FrameBgHovered] = ImVec4(r, g, b, a);
    c[ImGuiCol_FrameBgActive] = ImVec4(r, g, b, a);
    // ScrollbarBg РІР‚вЂќ use C_INPUT
    r = ((C_INPUT >> 0) & 0xFF) / 255.0f; g = ((C_INPUT >> 8) & 0xFF) / 255.0f;
    b = ((C_INPUT >> 16) & 0xFF) / 255.0f; a = ((C_INPUT >> 24) & 0xFF) / 255.0f;
    c[ImGuiCol_ScrollbarBg] = ImVec4(r, g, b, a);
}

static const float W = 700.0f;
static const float H = 432.0f;
static const float HEADER_H = 50.0f;
static const float CORNER = 12.0f;

extern HMODULE g_hModule;

static std::string FormatNumber(int num) {
    std::string s = std::to_string(num);
    int n = s.length() - 3;
    while (n > 0) {
        s.insert(n, ".");
        n -= 3;
    }
    return s;
}

static std::string ToLowerUTF8(const std::string& utf8Str) {
    if (utf8Str.empty()) return "";
    int wLen = MultiByteToWideChar(CP_UTF8, 0, utf8Str.c_str(), -1, NULL, 0);
    if (wLen <= 0) return utf8Str;
    std::wstring wStr(wLen, 0);
    MultiByteToWideChar(CP_UTF8, 0, utf8Str.c_str(), -1, &wStr[0], wLen);
    
    CharLowerBuffW(&wStr[0], wStr.length());
    
    int aLen = WideCharToMultiByte(CP_UTF8, 0, wStr.c_str(), -1, NULL, 0, NULL, NULL);
    if (aLen <= 0) return utf8Str;
    std::string aStr(aLen, 0);
    WideCharToMultiByte(CP_UTF8, 0, wStr.c_str(), -1, &aStr[0], aLen, NULL, NULL);
    // Remove the null terminator that might be included by calculation
    if (!aStr.empty() && aStr.back() == '\0') aStr.pop_back();
    return aStr;
}

std::string Gui::GetCurrentDateTime() {
    time_t now = time(0);
    struct tm t; localtime_s(&t, &now);
    char buf[64];
    sprintf_s(buf, "%04d-%02d-%02d %02d:%02d", t.tm_year+1900, t.tm_mon+1, t.tm_mday, t.tm_hour, t.tm_min);
    return std::string(buf);
}

// ===== Data Loading =====
void Gui::LoadVersion() {
    // Try Settings.json first (written by launcher with version field)
    std::string sp = GetAppDataPath() + "Settings.json";
    std::ifstream sf(sp);
    if (sf.is_open()) {
        try { json j; sf >> j; if (j.contains("Version")) { versionStr = j["Version"].get<std::string>(); return; } } catch (...) {}
    }
    // Fallback: version.json
    std::string p = GetAppDataPath() + "version.json";
    std::ifstream f(p); if (!f.is_open()) return;
    try { json j; f >> j; versionStr = j.value("version", "?.?.?"); } catch (...) {}
}

std::vector<NoteSegment> ParseRtfToSegments(const std::string& rtf) {
    std::vector<NoteSegment> segments;
    if (rtf.empty()) return segments;

    ImU32 currentColor = IM_COL32(255, 255, 255, 255);
    std::vector<ImU32> colors;
    
    size_t colorTblStart = rtf.find("\\colortbl");
    if (colorTblStart != std::string::npos) {
        size_t end = rtf.find("}", colorTblStart);
        if (end != std::string::npos) {
            std::string tbl = rtf.substr(colorTblStart + 9, end - (colorTblStart + 9));
            if (!tbl.empty() && tbl[0] == ';') {
                colors.push_back(currentColor);
                tbl = tbl.substr(1);
            }
            std::string currentItem = "";
            for (char c : tbl) {
                if (c == ';') {
                    if (currentItem.empty()) {
                        colors.push_back(currentColor);
                    } else {
                        int r = 255, g = 255, b = 255;
                        size_t rp = currentItem.find("\\red");
                        if (rp != std::string::npos) r = std::stoi(currentItem.substr(rp + 4));
                        size_t gp = currentItem.find("\\green");
                        if (gp != std::string::npos) g = std::stoi(currentItem.substr(gp + 6));
                        size_t bp = currentItem.find("\\blue");
                        if (bp != std::string::npos) b = std::stoi(currentItem.substr(bp + 5));
                        colors.push_back(IM_COL32(r, g, b, 255));
                    }
                    currentItem.clear();
                } else {
                    currentItem += c;
                }
            }
        }
    }

    struct RtfState {
        ImU32 color;
        bool bold;
        bool italic;
        bool underline;
        int alignment;
    };
    std::vector<RtfState> stateStack;

    NoteSegment currentSeg;
    currentSeg.color = currentColor;
    currentSeg.bold = false;
    currentSeg.italic = false;
    currentSeg.underline = false;
    currentSeg.alignment = 0;
    currentSeg.text = "";

    auto pushSegment = [&]() {
        if (!currentSeg.text.empty()) {
            segments.push_back(currentSeg);
            currentSeg.text.clear();
        }
    };

    size_t i = 0;
    int ignoreDepth = 0;
    
    while (i < rtf.length()) {
        char c = rtf[i];
        if (c == '{') {
            stateStack.push_back({currentSeg.color, currentSeg.bold, currentSeg.italic, currentSeg.underline, currentSeg.alignment});
            i++;
            bool isIgnored = (ignoreDepth > 0);
            if (!isIgnored && i < rtf.length() && rtf[i] == '\\') {
                size_t cmdEnd = i + 1;
                while (cmdEnd < rtf.length() && std::isalpha(rtf[cmdEnd])) cmdEnd++;
                std::string cmd = rtf.substr(i + 1, cmdEnd - i - 1);
                if (cmd == "fonttbl" || cmd == "colortbl" || cmd == "stylesheet" || cmd == "info" || cmd == "author" || cmd == "title" || cmd == "subject") {
                    isIgnored = true;
                }
            }
            if (isIgnored) {
                ignoreDepth++;
            }
        } else if (c == '}') {
            if (ignoreDepth > 0) {
                ignoreDepth--;
                if (!stateStack.empty()) stateStack.pop_back();
            } else {
                pushSegment();
                if (!stateStack.empty()) {
                    RtfState s = stateStack.back();
                    stateStack.pop_back();
                    currentSeg.color = s.color;
                    currentSeg.bold = s.bold;
                    currentSeg.italic = s.italic;
                    currentSeg.underline = s.underline;
                    currentSeg.alignment = s.alignment;
                }
            }
            i++;
        } else if (c == '\\') {
            if (ignoreDepth > 0) { i++; continue; }
            i++;
            if (i >= rtf.length()) break;
            
            if (rtf[i] == '\\' || rtf[i] == '{' || rtf[i] == '}') {
                currentSeg.text += rtf[i];
                i++;
                continue;
            }
            if (rtf[i] == '\'') {
                i++;
                if (i + 1 < rtf.length()) {
                    std::string hexStr = rtf.substr(i, 2);
                    try {
                        char cc = (char)std::stoi(hexStr, nullptr, 16);
                        currentSeg.text += cc;
                    } catch(...) {}
                    i += 2;
                }
                continue;
            }
            
            std::string cmd = "";
            while (i < rtf.length() && std::isalpha(rtf[i])) {
                cmd += rtf[i];
                i++;
            }
            
            bool hasParam = false;
            int param = 0;
            bool isNegative = false;
            if (i < rtf.length() && rtf[i] == '-') {
                isNegative = true;
                i++;
            }
            std::string paramStr = "";
            while (i < rtf.length() && std::isdigit(rtf[i])) {
                paramStr += rtf[i];
                i++;
            }
            if (!paramStr.empty()) {
                hasParam = true;
                try { param = std::stoi(paramStr); } catch(...) { param = 0; }
                if (isNegative) param = -param;
            }
            
            if (i < rtf.length() && rtf[i] == ' ') {
                i++;
            }

            if (cmd == "par" || cmd == "line") {
                pushSegment();
                currentSeg.text = "\n";
                pushSegment();
            } else if (cmd == "b") {
                pushSegment();
                currentSeg.bold = (!hasParam || param != 0);
            } else if (cmd == "i") {
                pushSegment();
                currentSeg.italic = (!hasParam || param != 0);
            } else if (cmd == "ul") {
                pushSegment();
                currentSeg.underline = (!hasParam || param != 0);
            } else if (cmd == "ulnone") {
                pushSegment();
                currentSeg.underline = false;
            } else if (cmd == "cf") {
                pushSegment();
                if (hasParam && param >= 0 && (size_t)param < colors.size()) {
                    currentSeg.color = colors[param];
                }
            } else if (cmd == "qc") {
                pushSegment();
                currentSeg.alignment = 1;
                for (int j = (int)segments.size() - 1; j >= 0; j--) {
                    if (segments[j].text.find('\n') != std::string::npos) break;
                    segments[j].alignment = 1;
                }
            } else if (cmd == "qr") {
                pushSegment();
                currentSeg.alignment = 2;
                for (int j = (int)segments.size() - 1; j >= 0; j--) {
                    if (segments[j].text.find('\n') != std::string::npos) break;
                    segments[j].alignment = 2;
                }
            } else if (cmd == "ql") {
                pushSegment();
                currentSeg.alignment = 0;
                for (int j = (int)segments.size() - 1; j >= 0; j--) {
                    if (segments[j].text.find('\n') != std::string::npos) break;
                    segments[j].alignment = 0;
                }
            } else if (cmd == "u") {
                if (hasParam) {
                    int codepoint = param;
                    if (codepoint < 0) codepoint += 65536;
                    std::string utf8;
                    if (codepoint <= 0x7F) {
                        utf8 += (char)codepoint;
                    } else if (codepoint <= 0x7FF) {
                        utf8 += (char)(0xC0 | ((codepoint >> 6) & 0x1F));
                        utf8 += (char)(0x80 | (codepoint & 0x3F));
                    } else if (codepoint <= 0xFFFF) {
                        utf8 += (char)(0xE0 | ((codepoint >> 12) & 0x0F));
                        utf8 += (char)(0x80 | ((codepoint >> 6) & 0x3F));
                        utf8 += (char)(0x80 | (codepoint & 0x3F));
                    }
                    currentSeg.text += utf8;
                    if (i < rtf.length() && rtf[i] == '?') i++;
                }
            } else if (cmd == "tab") {
                currentSeg.text += "\t";
            }
        } else {
            if (ignoreDepth == 0) {
                if (c != '\r' && c != '\n') {
                    currentSeg.text += c;
                }
            }
            i++;
        }
    }
    pushSegment();
    return segments;
}

void Gui::LoadLaws() {
    lawSections.clear();
    std::string p = GetAppDataPath() + "laws.json";
    std::ifstream f(p); if (!f.is_open()) return;
    try {
        json j; f >> j;
        auto parseSection = [](const json& sec, const std::string& name) -> LawSection {
            LawSection ls; ls.name = name;
            ls.type = (sec.contains("Type") && sec["Type"].is_string()) ? sec["Type"].get<std::string>() : "laws";
            ls.hasPunishments = (sec.contains("HasPunishments") && sec["HasPunishments"].is_boolean()) ? sec["HasPunishments"].get<bool>() : true;
            if (ls.type == "text") {
                ls.content = (sec.contains("Content") && sec["Content"].is_string()) ? sec["Content"].get<std::string>() : "";
                ls.rtfData = (sec.contains("RtfData") && sec["RtfData"].is_string()) ? sec["RtfData"].get<std::string>() : "";
                if (!ls.rtfData.empty()) {
                    ls.noteSegments = ParseRtfToSegments(ls.rtfData);
                } else {
                    // Fallback to plain text segment
                    NoteSegment ns;
                    ns.text = ls.content;
                    ns.color = IM_COL32(255, 255, 255, 255);
                    ns.bold = false;
                    ns.italic = false;
                    ns.underline = false;
                    ns.alignment = 0;
                    ls.noteSegments.push_back(ns);
                }
            } else {
                if (sec.contains("Items")) {
                    for (auto& item : sec["Items"]) {
                        LawItem law;
                        law.type = (item.contains("type") && item["type"].is_string()) ? item["type"].get<std::string>() : "";
                        law.id = (item.contains("id") && item["id"].is_string()) ? item["id"].get<std::string>() : "";
                        law.txt = (item.contains("txt") && item["txt"].is_string()) ? item["txt"].get<std::string>() : "";
                        law.pun = (item.contains("pun") && item["pun"].is_string()) ? item["pun"].get<std::string>() : "";
                        
                        law.level = 0;
                        if (item.contains("col")) {
                            if (item["col"].is_number()) law.level = item["col"].get<int>();
                            else if (item["col"].is_string()) {
                                try { law.level = std::stoi(item["col"].get<std::string>()); } catch(...) {}
                            }
                        }
                        
                        ls.items.push_back(law);
                    }
                }
            }
            return ls;
        };
        // Try common keys
        struct { const char* key; const char* label; } sections[] = {
            {"\xD0\xA3\xD0\x9A \xD0\xA0\xD0\xA4", "\xD0\xA3\xD0\xB3\xD0\xBE\xD0\xBB\xD0\xBE\xD0\xB2\xD0\xBD\xD1\x8B\xD0\xB9 \xD0\x9A\xD0\xBE\xD0\xB4\xD0\xB5\xD0\xBA\xD1\x81 (\xD0\xA3\xD0\x9A)"},
            {"\xD0\xA3\xD0\x9A", "\xD0\xA3\xD0\xB3\xD0\xBE\xD0\xBB\xD0\xBE\xD0\xB2\xD0\xBD\xD1\x8B\xD0\xB9 \xD0\x9A\xD0\xBE\xD0\xB4\xD0\xB5\xD0\xBA\xD1\x81 (\xD0\xA3\xD0\x9A)"},
            {"\xD0\x9A\xD0\xBE\xD0\x90\xD0\x9F", "\xD0\x9A\xD0\xBE\xD0\x90\xD0\x9F"},
        };
        for (auto& s : sections) {
            if (j.contains(s.key)) {
                lawSections.push_back(parseSection(j[s.key], s.label));
            }
        }
        // Add any other top-level keys
        for (auto& [key, val] : j.items()) {
            bool found = false;
            for (auto& s : sections) if (key == s.key) { found = true; break; }
            if (!found) {
                lawSections.push_back(parseSection(val, key));
            }
        }
    } catch (...) {}
}

void Gui::LoadFines() {
    fineItems.clear();
    std::string p = GetAppDataPath() + "fines.json";
    std::ifstream f(p); if (!f.is_open()) return;
    try {
        json j; f >> j;
        if (j.contains("items")) {
            for (auto& item : j["items"]) {
                FineItem fi;
                std::string rawId = (item.contains("id") && item["id"].is_string()) ? item["id"].get<std::string>() : "";
                fi.id = rawId;
                fi.type = (item.contains("type") && item["type"].is_string()) ? item["type"].get<std::string>() : "";
                if (fi.type == "\xD0\xA3\xD0\x9A \xD0\xA0\xD0\xA4") fi.type = "\xD0\xA3\xD0\x9A"; // "РЈРљ Р Р¤" -> "РЈРљ"
                fi.name = (item.contains("name") && item["name"].is_string()) ? item["name"].get<std::string>() : "";
                fi.amount = (item.contains("amount") && item["amount"].is_number()) ? item["amount"].get<int>() : 0;
                fi.hasLicRevoke = (item.contains("revoke") && item["revoke"].is_boolean()) ? item["revoke"].get<bool>() : false;
                fi.selected = false;
                fineItems.push_back(fi);
            }
        }
    } catch (...) {}
}

// ===== Style =====
void Gui::SetupStyle() {
    ImGuiStyle& style = ImGui::GetStyle();
    style.WindowRounding = 0; style.ChildRounding = 6; style.FrameRounding = 6;
    style.WindowBorderSize = 0; style.FrameBorderSize = 0;
    style.WindowPadding = ImVec2(0, 0); style.FramePadding = ImVec2(10, 7);
    style.ItemSpacing = ImVec2(8, 6); 
    style.ScrollbarSize = 6.0f;
    style.ScrollbarRounding = 3.0f;
    ImVec4* c = style.Colors;
    c[ImGuiCol_Text] = ImVec4(0.95f,0.95f,0.95f,1);
    c[ImGuiCol_WindowBg] = ImVec4(0,0,0,0);
    c[ImGuiCol_ChildBg] = ImVec4(0,0,0,0);
    c[ImGuiCol_FrameBg] = ImVec4(0.067f,0.082f,0.114f,1);
    c[ImGuiCol_FrameBgHovered] = ImVec4(0.12f,0.14f,0.18f,1);
    c[ImGuiCol_FrameBgActive] = ImVec4(0.12f,0.14f,0.18f,1);
    c[ImGuiCol_ScrollbarBg] = ImVec4(0.067f,0.082f,0.114f,1);
    c[ImGuiCol_ScrollbarGrab] = ImVec4(0.42f, 0.45f, 0.50f, 1.0f); // #6b737f
    c[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.55f, 0.58f, 0.62f, 1.0f);
    c[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.70f, 0.73f, 0.77f, 1.0f);
}

// ===== Init =====
void Gui::Init(IDirect3DDevice9* pDevice) {
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    D3DDEVICE_CREATION_PARAMETERS cp;
    pDevice->GetCreationParameters(&cp);
    ImGui_ImplWin32_Init(cp.hFocusWindow);
    ImGui_ImplDX9_Init(pDevice);
    ImFontConfig fc;
    fc.OversampleH = 2; fc.OversampleV = 2; fc.PixelSnapH = true;

    // Clear and Load fonts
    io.Fonts->Clear();
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\arial.ttf", 16.0f, &fc, io.Fonts->GetGlyphRangesCyrillic()); // Base font
    
    fontArialBlack24 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\ariblk.ttf", 24.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontArialBlack24) fontArialBlack24 = io.Fonts->Fonts[0];

    fontSegoeBold12 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeuib.ttf", 12.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontSegoeBold12) fontSegoeBold12 = io.Fonts->Fonts[0];

    fontSegoeBold14 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeuib.ttf", 14.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontSegoeBold14) fontSegoeBold14 = io.Fonts->Fonts[0];

    fontSegoeBold20 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeuib.ttf", 20.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontSegoeBold20) fontSegoeBold20 = io.Fonts->Fonts[0];

    fontSegoeBlack32 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\seguibl.ttf", 28.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontSegoeBlack32) fontSegoeBlack32 = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeuib.ttf", 28.0f, &fc, io.Fonts->GetGlyphRangesCyrillic());
    if (!fontSegoeBlack32) fontSegoeBlack32 = io.Fonts->Fonts[0];

    io.ConfigFlags |= ImGuiConfigFlags_NoMouseCursorChange;
    SetupStyle();
    LoadVersion();
    LoadSettings();
    LoadLaws();
    LoadFines();
    LoadRadialConfig();
}

// ===== Toggle =====
static float savedSensX = -1.0f, savedSensY = -1.0f;

void Gui::Toggle() {
    // Center OS cursor BEFORE toggling (hook blocks SetCursorPos when show==true)
    if (!show && g_hWnd) {
        RECT rc;
        if (GetClientRect(g_hWnd, &rc)) {
            POINT center = { (rc.right - rc.left) / 2, (rc.bottom - rc.top) / 2 };
            ClientToScreen(g_hWnd, &center);
            SetCursorPos(center.x, center.y);
        }
    }
    show = !show;
    if (show) {
        savedSensX = *(float*)0xB6EC1C;
        savedSensY = *(float*)0xB6EC18;
        clearNextFrame = true; // Signal Render() to aggressively clear text inputs
    } else {
        if (savedSensX >= 0.0f) *(float*)0xB6EC1C = savedSensX;
        if (savedSensY >= 0.0f) *(float*)0xB6EC18 = savedSensY;
        if (!rememberTab) {
            activeTab = 0;         // Reset to Laws tab
            showSettings = false;  // Also close settings panel
        }
        showLawDropdown = false;
        SetCursor(NULL);
        
        if (g_hWnd) {
            BYTE keys[] = { 'W','A','S','D', VK_SPACE, VK_SHIFT, VK_CONTROL,
                            VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT, VK_LBUTTON, VK_RBUTTON };
            for (BYTE vk : keys)
                PostMessage(g_hWnd, WM_KEYUP, vk, (MapVirtualKey(vk, MAPVK_VK_TO_VSC) << 16) | 0xC0000001);
        }
    }
}

// ===== Dashed Line Helper =====
static void DrawDashedLine(ImDrawList* dl, ImVec2 a, ImVec2 b, ImU32 col, float dashLen = 2.0f, float gapLen = 4.0f) {
    float dx = b.x - a.x, dy = b.y - a.y;
    float len = sqrtf(dx*dx + dy*dy);
    if (len < 1.0f) return;
    float ux = dx/len, uy = dy/len;
    float pos = 0;
    while (pos < len) {
        float end = pos + dashLen; if (end > len) end = len;
        dl->AddLine(ImVec2(a.x + ux*pos, a.y + uy*pos), ImVec2(a.x + ux*end, a.y + uy*end), col, 1.0f);
        pos = end + gapLen;
    }
}

// ===== HUD Frame Drawing =====
void Gui::DrawHudFrame(ImDrawList* dl, ImVec2 o) {
    // Clipped-corner background
    ImVec2 poly[] = {
        {o.x, o.y+CORNER}, {o.x+CORNER, o.y}, {o.x+W-CORNER, o.y}, {o.x+W, o.y+CORNER},
        {o.x+W, o.y+H-CORNER}, {o.x+W-CORNER, o.y+H}, {o.x+CORNER, o.y+H}, {o.x, o.y+H-CORNER}
    };
    dl->AddConvexPolyFilled(poly, 8, IM_COL32(((C_BG>>0)&0xFF), ((C_BG>>8)&0xFF), ((C_BG>>16)&0xFF), (int)(settingsAlpha * 255)));

    // Grid pattern with geometric clipping to the 12px corners to prevent phantom 90 degree corners
    for (float x = 0; x < W; x += 30) {
        float startY = 0, endY = H;
        if (x < CORNER) { startY = CORNER - x; endY = H - (CORNER - x); }
        else if (x > W - CORNER) { startY = x - (W - CORNER); endY = H - (x - (W - CORNER)); }
        dl->AddLine(ImVec2(o.x+x, o.y+startY), ImVec2(o.x+x, o.y+endY), C_GRID, 1.0f);
    }
    for (float y = 0; y < H; y += 30) {
        float startX = 0, endX = W;
        if (y < CORNER) { startX = CORNER - y; endX = W - (CORNER - y); }
        else if (y > H - CORNER) { startX = y - (H - CORNER); endX = W - (y - (H - CORNER)); }
        dl->AddLine(ImVec2(o.x+startX, o.y+y), ImVec2(o.x+endX, o.y+y), C_GRID, 1.0f);
    }

    // Border (drawn with individual lines to prevent sharp miter spikes at clipped corners)
    for (int i = 0; i < 8; i++) {
        dl->AddLine(poly[i], poly[(i+1)%8], C_LINE, 2.0f);
    }

    // Gold accent top-left
    dl->AddLine(ImVec2(o.x, o.y+CORNER), ImVec2(o.x+CORNER, o.y), C_GOLD, 2.0f);
    dl->AddLine(ImVec2(o.x+CORNER, o.y), ImVec2(o.x+250, o.y), C_GOLD, 2.0f);

    // Header bar (polygon so it doesn't overlap clipped corners)
    ImVec2 hdrPoly[] = {
        {o.x, o.y+CORNER}, {o.x+CORNER, o.y}, {o.x+W-CORNER, o.y}, {o.x+W, o.y+CORNER},
        {o.x+W, o.y+HEADER_H}, {o.x, o.y+HEADER_H}
    };
    dl->AddConvexPolyFilled(hdrPoly, 6, C_HEADER);
    dl->AddLine(ImVec2(o.x, o.y+HEADER_H), ImVec2(o.x+W, o.y+HEADER_H), C_BORDER, 2.0f);

    // "DURAN HELPER" - SVG: x=20 y=32 font-size=20 (using size 24.0f to match visual weight)
    dl->AddText(fontArialBlack24, 24.0f, ImVec2(o.x+20, o.y+10), C_WHITE, "DURAN");
    float duranW = fontArialBlack24->CalcTextSizeA(24.0f, FLT_MAX, 0.0f, "DURAN ").x;
    dl->AddText(fontArialBlack24, 24.0f, ImVec2(o.x+20+duranW, o.y+10), C_GOLD, "HELPER");
    float helperW = fontArialBlack24->CalcTextSizeA(24.0f, FLT_MAX, 0.0f, "HELPER").x;

    // Version badge
    std::string ver = "V" + versionStr;
    float badgeX = o.x + 20 + duranW + helperW + 12;
    float verFontSize = 11.0f;
    float verW = fontSegoeBold12->CalcTextSizeA(verFontSize, FLT_MAX, 0.0f, ver.c_str()).x + 10.0f;
    float badgeH = 15.0f;
    float badgeY = o.y + 16.0f; // Aligned nicely with title
    dl->AddRectFilled(ImVec2(badgeX, badgeY), ImVec2(badgeX + verW, badgeY + badgeH), C_RED_BG, 4.0f);
    dl->AddText(fontSegoeBold12, verFontSize, ImVec2(badgeX + 5.0f, badgeY + 1.0f), C_RED, ver.c_str());

    // Close button - x=655 y=12 w=26 h=26
    float bx = o.x+655, by = o.y+12;
    bool closeHover = ImGui::IsMouseHoveringRect(ImVec2(bx, by), ImVec2(bx+26, by+26), false);
    bool closeDown = closeHover && ImGui::IsMouseDown(0);
    ImU32 closeBg = closeDown ? IM_COL32(231,76,60,80) : (closeHover ? IM_COL32(255,255,255,10) : C_BOX);
    ImU32 closeFg = closeHover ? C_WHITE : C_GRAY;
    dl->AddRectFilled(ImVec2(bx, by), ImVec2(bx+26, by+26), closeBg, 4.0f);
    dl->AddRect(ImVec2(bx, by), ImVec2(bx+26, by+26), C_LINE, 4.0f); // Always draw border
    
    // Draw cross (AA disabled for pixel-perfect symmetry)
    auto oldFlags = dl->Flags;
    dl->Flags &= ~ImDrawListFlags_AntiAliasedLines;
    dl->AddLine(ImVec2(bx+8, by+8), ImVec2(bx+18, by+18), closeFg, 2.0f);
    dl->AddLine(ImVec2(bx+18, by+8), ImVec2(bx+8, by+18), closeFg, 2.0f);
    dl->Flags = oldFlags;
}

// ===== Tab Drawing ===== SVG: x=260 y=12 w=80,160,80 h=26
void Gui::DrawTabs(ImDrawList* dl, ImVec2 o) {
    struct TabDef { const char* label; float w; };
    TabDef tabs[] = {
        {"\xD0\x97\xD0\x90\xD0\x9A\xD0\x9E\xD0\x9D\xD0\xAB", 80},
        {"\xD0\x9A\xD0\x90\xD0\x9B\xD0\xAC\xD0\x9A\xD0\xa3\xD0\x9B\xD0\xAF\xD0\xa2\xD0\x9E\xD0\xa0 \xD0\xa8\xD0\xa2\xD0\xa0\xD0\x90\xD0\xa4\xD0\x9E\xD0\x92", 160},
        {"\xD0\x91\xD0\x98\xD0\x9D\xD0\x94\xD0\x95\xD0\xa0", 80},
    };
    float tx = o.x + 260;
    float ty = o.y + 12;
    for (int i = 0; i < 3; i++) {
        ImVec2 p0(tx, ty), p1(tx + tabs[i].w, ty + 26);
        bool active = (activeTab == i && !showSettings);
        bool hover = ImGui::IsMouseHoveringRect(p0, p1, false);
        if (active) {
            dl->AddRectFilled(p0, p1, C_GOLD, 4.0f);
        } else {
            dl->AddRectFilled(p0, p1, C_BOX, 4.0f);
            dl->AddRect(p0, p1, C_LINE, 4.0f);
        }
        ImVec2 ts = fontSegoeBold12->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, tabs[i].label);
        float cx = tx + (tabs[i].w - ts.x) * 0.5f;
        float cy = ty + (26 - ts.y) * 0.5f;
        dl->AddText(fontSegoeBold12, 11.0f, ImVec2(cx, cy), active ? C_DARK : (hover ? C_WHITE : C_GRAY), tabs[i].label);
        tx += tabs[i].w + 10;
    }

    // Settings gear icon РІР‚вЂќ right of last tab
    float gx = tx + 5, gy = o.y + 12;
    bool gearHover = ImGui::IsMouseHoveringRect(ImVec2(gx, gy), ImVec2(gx+26, gy+26), false);
    bool gearActive = showSettings;
    ImU32 gearBg = gearActive ? C_GOLD : (gearHover ? IM_COL32(255,255,255,10) : C_BOX);
    ImU32 gearFg = gearActive ? C_DARK : (gearHover ? C_WHITE : C_GRAY);
    dl->AddRectFilled(ImVec2(gx, gy), ImVec2(gx+26, gy+26), gearBg, 4.0f);
    if (!gearActive) dl->AddRect(ImVec2(gx, gy), ImVec2(gx+26, gy+26), C_LINE, 4.0f); // Keep border unless active
    
    // Draw 3 horizontal lines
    float lx = gx + 6, lw = 14;
    dl->AddLine(ImVec2(lx, gy + 8), ImVec2(lx + lw, gy + 8), gearFg, 2.0f);
    dl->AddLine(ImVec2(lx, gy + 13), ImVec2(lx + lw, gy + 13), gearFg, 2.0f);
    dl->AddLine(ImVec2(lx, gy + 18), ImVec2(lx + lw, gy + 18), gearFg, 2.0f);
}

// ===== Laws Tab ===== SVG: search x=25,y=80 w=450 h=34; dropdown x=495,y=80 w=320 h=34; articles y=135
void Gui::RenderLawsTab(ImDrawList* dl, ImVec2 o) {
    if (lawSections.empty()) selectedLawSection = 0;
    else if (selectedLawSection >= (int)lawSections.size()) selectedLawSection = lawSections.size() - 1;
    if (selectedLawSection < 0) selectedLawSection = 0;
    
    // Empty state
    if (lawSections.empty()) {
        // Disabled search bar (Matched active measurements)
        dl->AddRectFilled(ImVec2(o.x+20, o.y+65), ImVec2(o.x+460, o.y+95), C_HEADER, 6.0f);
        dl->AddRect(ImVec2(o.x+20, o.y+65), ImVec2(o.x+460, o.y+95), C_BORDER, 6.0f, 0, 1.5f);
        // Search icon
        dl->AddCircle(ImVec2(o.x+35, o.y+80), 4.0f, C_GRAY2, 12, 2.0f);
        dl->AddLine(ImVec2(o.x+38, o.y+83), ImVec2(o.x+42, o.y+87), C_GRAY2, 2.0f);
        dl->AddText(fontSegoeBold14, 14.0f, ImVec2(o.x+50, o.y+73), IM_COL32(74,80,89,255),
            "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD0\xBD\xD0\xB5\xD0\xB4\xD0\xBE\xD1\x81\xD1\x82\xD1\x83\xD0\xBF\xD0\xB5\xD0\xBD...");
        
        // Disabled dropdown - same pos/size as active (x=470, w=210)
        dl->AddRectFilled(ImVec2(o.x+470, o.y+65), ImVec2(o.x+680, o.y+95), C_HEADER, 6.0f);
        dl->AddRect(ImVec2(o.x+470, o.y+65), ImVec2(o.x+680, o.y+95), C_BORDER, 6.0f, 0, 1.5f);
        dl->AddText(fontSegoeBold14, 12.0f, ImVec2(o.x+485, o.y+75), IM_COL32(74,80,89,255),
            "\xD0\x91\xD0\xB0\xD0\xB7\xD0\xB0 \xD0\xBD\xD0\xB5 \xD0\xB2\xD1\x8B\xD0\xB1\xD1\x80\xD0\xB0\xD0\xBD\xD0\xB0");
        // Chevron
        dl->AddLine(ImVec2(o.x+662, o.y+77), ImVec2(o.x+668, o.y+83), IM_COL32(74,80,89,255), 2.0f);
        dl->AddLine(ImVec2(o.x+668, o.y+83), ImVec2(o.x+674, o.y+77), IM_COL32(74,80,89,255), 2.0f);
        
        // Center icon: document
        float icx = o.x + 350, icy = o.y + 230;
        // Document outline
        dl->AddLine(ImVec2(icx-25, icy-40), ImVec2(icx+10, icy-40), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx+10, icy-40), ImVec2(icx+30, icy-20), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx+30, icy-20), ImVec2(icx+30, icy+40), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx+30, icy+40), ImVec2(icx-35, icy+40), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx-35, icy+40), ImVec2(icx-35, icy-30), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx-35, icy-30), ImVec2(icx-25, icy-40), C_BORDER, 4.0f);
        // Fold corner
        dl->AddLine(ImVec2(icx+10, icy-40), ImVec2(icx+10, icy-20), C_BORDER, 4.0f);
        dl->AddLine(ImVec2(icx+10, icy-20), ImVec2(icx+30, icy-20), C_BORDER, 4.0f);
        // Text lines inside doc
        dl->AddLine(ImVec2(icx-15, icy-10), ImVec2(icx+15, icy-10), C_LINE, 4.0f);
        dl->AddLine(ImVec2(icx-15, icy+5), ImVec2(icx+15, icy+5), C_LINE, 4.0f);
        dl->AddLine(ImVec2(icx-15, icy+20), ImVec2(icx+5, icy+20), C_LINE, 4.0f);
        
        // Text
        const char* emtTxt = "\xD0\x91\xD0\x90\xD0\x97\xD0\x90 \xD0\x97\xD0\x90\xD0\x9A\xD0\x9E\xD0\x9D\xD0\x9E\xD0\x92 \xD0\x9F\xD0\xA3\xD0\xA1\xD0\xA2\xD0\x90"; // Р‘РђР—Рђ Р—РђРљРћРќРћР’ РџРЈРЎРўРђ
        ImVec2 t1 = fontSegoeBold20->CalcTextSizeA(16.0f, FLT_MAX, 0.0f, emtTxt);
        dl->AddText(fontSegoeBold20, 16.0f, ImVec2(icx - t1.x/2, icy+75), C_GRAY, emtTxt);
        
        const char* emtSub = "\xD0\x9E\xD1\x82\xD0\xBA\xD1\x80\xD0\xBE\xD0\xB9\xD1\x82\xD0\xB5 \xD0\xBB\xD0\xB0\xD1\x83\xD0\xBD\xD1\x87\xD0\xB5\xD1\x80 \xD0\xB8 \xD0\xBD\xD0\xB0\xD1\x81\xD1\x82\xD1\x80\xD0\xBE\xD0\xB9\xD1\x82\xD0\xB5 \xD0\xBF\xD1\x80\xD0\xBE\xD1\x84\xD0\xB8\xD0\xBB\xD1\x8C \xD1\x81 \xD0\xA3\xD0\x9A / \xD0\x9A\xD0\xBE\xD0\x90\xD0\x9F."; // РћС‚РєСЂРѕР№С‚Рµ Р»Р°СѓРЅС‡РµСЂ...
        ImVec2 t2 = fontSegoeBold14->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, emtSub);
        dl->AddText(fontSegoeBold14, 12.0f, ImVec2(icx - t2.x/2, icy+95), IM_COL32(92,99,112,255), emtSub);
        return;
    }
    
    float cx = o.x + 25, cy = o.y + 80;

    // Search bar
    ImGui::SetCursorScreenPos(ImVec2(o.x + 20, o.y + 65));
    ImGui::PushItemWidth(440);
    ImGui::PushStyleColor(ImGuiCol_FrameBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_Border, C_BORDER);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 6.0f);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 1.5f);
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(30.0f, 8.0f));
    ImGui::PushFont(fontSegoeBold14);
    ImGui::InputTextWithHint("##lawSearch", "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD0\xB8 \xD0\xB8\xD0\xBB\xD0\xB8 \xD0\xBD\xD0\xB0\xD0\xB7\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD1\x8F...", searchLaws, 256);
    ImGui::PopFont();
    ImGui::PopStyleVar(3);
    ImGui::PopStyleColor(2);
    ImGui::PopItemWidth();
    // Magnifying glass icon
    dl->AddCircle(ImVec2(o.x+35, o.y+81), 4.0f, C_GRAY, 12, 2.0f);
    dl->AddLine(ImVec2(o.x+38, o.y+84), ImVec2(o.x+42, o.y+88), C_GRAY, 2.0f);

    // Articles area - SVG: starts at y=110, columns at x=20 and x=360, each 330px wide
    float artY = o.y + 115;
    float artH = H - 110 - 15;
    float colW = 310.0f; // Adjusted from 320 to prevent scrollbar overlap
    std::string sq = ToLowerUTF8(searchLaws);

    ImGui::SetCursorScreenPos(ImVec2(o.x + 20, artY));
    
    // Custom scrollbar for Laws
    ImGui::PushStyleColor(ImGuiCol_ScrollbarBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabHovered, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabActive, C_GOLD);
    ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, 6.0f);
    
    ImGui::BeginChild("##LawsScroll", ImVec2(W - 40, artH), false, ImGuiWindowFlags_NoBackground);
    if (resetLawsScroll) {
        ImGui::SetScrollY(0.0f);
        resetLawsScroll = false;
    }
    
    static bool s_canQuote = false;
    if (ImGui::IsMouseClicked(0)) {
        s_canQuote = ImGui::IsWindowHovered();
    }
    
    ImDrawList* cdl = ImGui::GetWindowDrawList();

    auto buildExtendedQuote = [&](const LawSection& s, const std::string& lawId, const std::string& lawTxt, const std::string& lawPun, const std::string& header) {
        if (!quoteExtended) return lawTxt;
        std::string res = s.name + "\n";
        if (!header.empty()) res += header + "\n";
        if (!lawId.empty()) res += "\xD0\xA1\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD1\x8F - " + lawId + "\n";
        res += lawTxt;
        if (!lawPun.empty()) res += "\n\xD0\x9D\xD0\xB0\xD0\xBA\xD0\xB0\xD0\xB7\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 - " + lawPun;
        return res;
    };

    auto buildChapterQuote = [&](const LawSection& s, const std::string& chapterName) {
        std::string res = s.name + "\n" + chapterName + "\n";
        bool inChapter = false;
        for (const auto& item : s.items) {
            if (item.type == "head") {
                if (item.txt == chapterName) inChapter = true;
                else if (inChapter) break;
            } else if (inChapter && !item.txt.empty()) {
                if (!item.id.empty()) res += "\xD0\xA1\xD1\x82. " + item.id + " - ";
                res += item.txt + "\n";
                if (!item.pun.empty()) res += "\xD0\x9D\xD0\xB0\xD0\xBA\xD0\xB0\xD0\xB7\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 - " + item.pun + "\n";
            }
        }
        return res;
    };

    auto getHeaderForIndex = [&](const LawSection& s, int idx) {
        for (int i = idx; i >= 0; i--) {
            if (s.items[i].type == "head") return s.items[i].txt;
        }
        return std::string("");
    };

    if (!sq.empty()) {
        // Search: either cross-section or current-section only
        ImVec2 cp = ImGui::GetCursorScreenPos();
        float yOff = 0;
        
        int startSec = 0, endSec = (int)lawSections.size();
        if (searchCurrentSection && selectedLawSection >= 0 && selectedLawSection < (int)lawSections.size()) {
            startSec = selectedLawSection;
            endSec = selectedLawSection + 1;
        }
        
        for (int s = startSec; s < endSec; s++) {
            auto& sec = lawSections[s];
            if (sec.type == "text") continue; // SKIP NOTEPAD SECTIONS IN SEARCH
            
            std::string currentHeader = "";
            std::string lastPrintedHeader = "\n"; // Ensure this won't match a real empty or first header
            bool printedSectionLabel = false;
            
            for (auto& law : sec.items) {
                if (law.type == "head") {
                    currentHeader = law.txt;
                    // Check if search matches the head itself РІР‚вЂќ if so, show all items under it
                    if (ToLowerUTF8(law.txt).find(sq) != std::string::npos) {
                        // Print the section/header label but don't highlight the head text
                        if (lastPrintedHeader != currentHeader) {
                            float sy = cp.y + yOff;
                            std::string srcLabel = "\xD0\x98\xD0\xA1\xD0\xA2\xD0\x9E\xD0\xA7\xD0\x9D\xD0\x98\xD0\x9A: " + sec.name;
                            if (!currentHeader.empty()) srcLabel += " > " + currentHeader;
                            cdl->AddText(fontSegoeBold12, 11.0f, ImVec2(cp.x, sy), C_GOLD, srcLabel.c_str());
                            float labelW = fontSegoeBold12->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, srcLabel.c_str()).x;
                            cdl->AddLine(ImVec2(cp.x + labelW + 8, sy + 7), ImVec2(cp.x + W - 70, sy + 7), C_BORDER, 1.0f);
                            yOff += 22.0f;
                            lastPrintedHeader = currentHeader;
                            printedSectionLabel = true;
                        }
                    }
                    continue;
                }
                
                if (ToLowerUTF8(law.id).find(sq) == std::string::npos && ToLowerUTF8(law.txt).find(sq) == std::string::npos)
                    continue;

                // Match found! Check if we need to print the Header
                if (lastPrintedHeader != currentHeader) {
                    float sy = cp.y + yOff;
                    std::string srcLabel = "\xD0\x98\xD0\xA1\xD0\xA2\xD0\x9E\xD0\xA7\xD0\x9D\xD0\x98\xD0\x9A: " + sec.name; // "РРЎРўРћР§РќРРљ: "
                    if (!currentHeader.empty()) {
                        srcLabel += " > " + currentHeader;
                    }
                    
                    cdl->AddText(fontSegoeBold12, 11.0f, ImVec2(cp.x, sy), C_GOLD, srcLabel.c_str());
                    float labelW = fontSegoeBold12->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, srcLabel.c_str()).x;
                    cdl->AddLine(ImVec2(cp.x + labelW + 8, sy + 7), ImVec2(cp.x + W - 70, sy + 7), C_BORDER, 1.0f);
                    yOff += 22.0f;
                    
                    lastPrintedHeader = currentHeader;
                    printedSectionLabel = true;
                }

                float y = cp.y + yOff;
                float fullW = W - 60;
                float idW = law.id.empty() ? 0.0f : (fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, law.id.c_str()).x + 8);
                float punW = law.pun.empty() ? 0.0f : fontSegoeBold14->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, law.pun.c_str()).x;
                float maxTxtW = !law.pun.empty() ? (fullW - idW - punW - 24.0f) : (fullW - idW - 10.0f);
                if (maxTxtW < 100.0f) maxTxtW = 100.0f;
                ImVec2 txtSz = fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, maxTxtW, law.txt.c_str());
                
                float blockCenterY = y + txtSz.y * 0.5f;
                
                // Smart Quote hover logic
                if (quoteEnabled) {
                    ImVec2 pMin(cp.x, y - 2.0f);
                    ImVec2 pMax(cp.x + fullW + 5.0f, y + (txtSz.y > 15.0f ? txtSz.y + 6.0f : 16.0f));
                    if (ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(pMin, pMax, false)) {
                        cdl->AddRectFilled(pMin, pMax, IM_COL32(210, 166, 94, 40), 6.0f);
                        ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                        if (ImGui::IsMouseReleased(0) && s_canQuote) {
                            ExecuteLawQuote(buildExtendedQuote(sec, law.id, law.txt, law.pun, currentHeader));
                        }
                    }
                }

                if (!law.id.empty()) {
                    cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(cp.x, blockCenterY - 6.0f), C_GOLD, law.id.c_str());
                }
                // Draw full text in white first (preserves exact layout/wrapping)
                cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(cp.x + idW, y), C_WHITE, law.txt.c_str(), nullptr, maxTxtW);
                
                // Overlay matching substrings in gold on top (no layout impact)
                {
                    std::string txtLower = ToLowerUTF8(law.txt);
                    float lineY = y;
                    float lineH = 14.0f * (13.0f / 14.0f);
                    float scale = 13.0f / 14.0f;
                    size_t queryLen = sq.length();

                    // Split text by \n first, then word-wrap within each segment
                    size_t segStart = 0;
                    while (segStart <= law.txt.length()) {
                        size_t nlPos = law.txt.find('\n', segStart);
                        if (nlPos == std::string::npos) nlPos = law.txt.length();
                        
                        // Current segment: [segStart, nlPos) РІР‚вЂќ one \n-delimited line
                        const char* segPtr = law.txt.c_str() + segStart;
                        const char* segEnd = law.txt.c_str() + nlPos;
                        
                        if (segStart < nlPos) {
                            // Word-wrap within this segment
                            const char* ws = segPtr;
                            while (ws < segEnd) {
                                const char* wrapPos = fontSegoeBold14->CalcWordWrapPositionA(scale, ws, segEnd, maxTxtW);
                                if (!wrapPos || wrapPos <= ws) wrapPos = segEnd;
                                
                                const char* dispEnd = wrapPos;
                                while (dispEnd > ws && (*(dispEnd - 1) == ' ')) dispEnd--;
                                
                                size_t lnStart = (size_t)(ws - law.txt.c_str());
                                size_t lnEnd = (size_t)(dispEnd - law.txt.c_str());
                                
                                if (lnStart < lnEnd) {
                                    size_t matchPos = txtLower.find(sq, lnStart);
                                    while (matchPos != std::string::npos && matchPos < lnEnd) {
                                        size_t mEnd = matchPos + queryLen;
                                        size_t drawStart = (lnStart > matchPos) ? lnStart : matchPos;
                                        size_t drawEnd = (lnEnd < mEnd) ? lnEnd : mEnd;
                                        
                                        if (drawStart < drawEnd) {
                                            std::string prefix = law.txt.substr(lnStart, drawStart - lnStart);
                                            float xOff = fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, prefix.c_str()).x;
                                            std::string matchStr = law.txt.substr(drawStart, drawEnd - drawStart);
                                            float mWidth = fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, matchStr.c_str()).x;
                                            
                                            float mx = cp.x + idW + xOff;
                                            cdl->AddRectFilled(ImVec2(mx, lineY - 1), ImVec2(mx + mWidth, lineY + 14), C_GOLD_BG, 2.0f);
                                            cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(mx, lineY), C_GOLD, matchStr.c_str());
                                        }
                                        
                                        if (mEnd > lnEnd) break;
                                        matchPos = txtLower.find(sq, mEnd);
                                    }
                                }
                                
                                ws = wrapPos;
                                while (ws < segEnd && *ws == ' ') ws++;
                                lineY += lineH;
                            }
                        } else {
                            // Empty line (standalone \n) РІР‚вЂќ just advance Y
                            lineY += lineH;
                        }
                        
                        segStart = nlPos + 1;
                    }
                }
                
                float blockCenterY_s = y + txtSz.y * 0.5f;
                if (!law.pun.empty()) {
                    float punX = cp.x + fullW - punW;
                    float lineStartX = cp.x + idW + txtSz.x + 8.0f;
                    float lineEndX = punX - 8.0f;
                    if (lineStartX < lineEndX) {
                        DrawDashedLine(cdl, ImVec2(lineStartX, blockCenterY_s), ImVec2(lineEndX, blockCenterY_s), C_LINE, 4, 4);
                    }
                    cdl->AddText(fontSegoeBold14, 12.0f, ImVec2(punX, blockCenterY_s - 6.0f), C_RED, law.pun.c_str());
                }
                yOff += (txtSz.y > 15.0f ? txtSz.y + 10.0f : 20.0f);
            }
            if (printedSectionLabel) {
                yOff += 10.0f; // gap between sections
            }
        }
        ImGui::Dummy(ImVec2(0, yOff));
    } else if (selectedLawSection >= 0 && selectedLawSection < (int)lawSections.size()) {
        auto& sec = lawSections[selectedLawSection];
        if (sec.type == "text") {
            struct DrawCmd {
                std::string text;
                ImFont* font;
                float fontSize;
                ImU32 color;
                bool underline;
                float w;
            };
            struct DrawLine {
                std::vector<DrawCmd> cmds;
                float totalW = 0;
                int alignment = 0;
            };
            std::vector<DrawLine> lines;
            DrawLine currentLine;
            
            float maxW = W - 65.0f;
            
            for (const auto& seg : sec.noteSegments) {
                ImFont* f = seg.bold ? fontSegoeBold14 : fontSegoeBold12;
                float fSize = seg.bold ? 14.0f : 13.0f;
                ImU32 c = seg.color;
                
                size_t start = 0;
                while (start < seg.text.length()) {
                    size_t nextSpace = seg.text.find_first_of(" \n\t", start);
                    bool isNewline = false;
                    bool isTab = false;
                    if (nextSpace == std::string::npos) nextSpace = seg.text.length();
                    else if (seg.text[nextSpace] == '\n') isNewline = true;
                    else if (seg.text[nextSpace] == '\t') isTab = true;
                    
                    std::string word = seg.text.substr(start, nextSpace - start + (isNewline || isTab ? 0 : 1));
                    
                    if (!word.empty()) {
                        float wordW = f->CalcTextSizeA(fSize, FLT_MAX, 0.0f, word.c_str()).x;
                        if (currentLine.totalW + wordW > maxW && currentLine.totalW > 0) {
                            lines.push_back(currentLine);
                            currentLine = DrawLine();
                            currentLine.alignment = seg.alignment;
                        }
                        currentLine.cmds.push_back({word, f, fSize, c, seg.underline, wordW});
                        currentLine.totalW += wordW;
                        currentLine.alignment = seg.alignment;
                    }
                    
                    if (isNewline) {
                        lines.push_back(currentLine);
                        currentLine = DrawLine();
                        currentLine.alignment = seg.alignment;
                    } else if (isTab) {
                        float tabW = f->CalcTextSizeA(fSize, FLT_MAX, 0.0f, "    ").x;
                        currentLine.cmds.push_back({"    ", f, fSize, c, false, tabW});
                        currentLine.totalW += tabW;
                    }
                    
                    start = nextSpace + 1;
                }
            }
            if (!currentLine.cmds.empty() || lines.empty()) lines.push_back(currentLine);
            
            ImVec2 pos = ImGui::GetCursorScreenPos();
            float startY = pos.y;
            float curY = 0;
            float lineH = 18.0f;
            
            for (auto& l : lines) {
                float startX = pos.x;
                if (l.alignment == 1) { // Center
                    float shift = (maxW - l.totalW) / 2.0f;
                    startX = pos.x + (shift > 0.0f ? shift : 0.0f);
                } else if (l.alignment == 2) { // Right
                    float shift = maxW - l.totalW;
                    startX = pos.x + (shift > 0.0f ? shift : 0.0f);
                }
                
                float curX = 0;
                for (auto& cmd : l.cmds) {
                    dl->PushTextureID(cmd.font->OwnerAtlas->TexID);
                    dl->AddText(cmd.font, cmd.fontSize, ImVec2(startX + curX, startY + curY), cmd.color, cmd.text.c_str());
                    if (cmd.underline) {
                        dl->AddLine(ImVec2(startX + curX, startY + curY + cmd.fontSize + 1.0f), ImVec2(startX + curX + cmd.w, startY + curY + cmd.fontSize + 1.0f), cmd.color, 1.0f);
                    }
                    dl->PopTextureID();
                    curX += cmd.w;
                }
                curY += lineH;
            }
            
            ImGui::Dummy(ImVec2(0, curY + 30.0f)); 
        } else if (sec.type == "1col") {
            // 1-column layout: all items rendered in a single full-width column
            auto& items = sec.items;
            float fullColW = W - 60.0f;
            std::string currentHeader = "";

            auto drawItem1col = [&](const LawItem& law, float x, float& yOff) {
                float y = ImGui::GetCursorScreenPos().y + yOff;
                if (law.type == "head") {
                    currentHeader = law.txt;
                    cdl->AddText(fontSegoeBold12, 12.0f, ImVec2(x, y), C_GOLD, law.txt.c_str());
                    cdl->AddLine(ImVec2(x, y+18), ImVec2(x+fullColW, y+18), C_BORDER, 1.5f);
                    
                    if (quoteEnabled && quoteChapter) {
                        ImVec2 pMin(x, y);
                        ImVec2 pMax(x + fullColW, y + 18.0f);
                        if (ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(pMin, pMax, false)) {
                            cdl->AddRectFilled(pMin, pMax, IM_COL32(210, 166, 94, 40), 4.0f);
                            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                            if (ImGui::IsMouseReleased(0) && s_canQuote) {
                                ExecuteLawQuote(buildChapterQuote(sec, law.txt));
                            }
                        }
                    }
                    
                    yOff += 30.0f;
                } else if (law.id.empty() && law.txt.empty() && law.pun.empty()) {
                    yOff += 20.0f;
                } else {
                    float idW = law.id.empty() ? 0.0f : (fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, law.id.c_str()).x + 8);
                    float punW = law.pun.empty() ? 0.0f : fontSegoeBold14->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, law.pun.c_str()).x;
                    
                    float maxTxtW = !law.pun.empty() ? (fullColW - idW - punW - 24.0f) : (fullColW - idW - 10.0f);
                    if (maxTxtW < 100.0f) maxTxtW = 100.0f;

                    ImVec2 txtSz = fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, maxTxtW, law.txt.c_str());
                    float blockCenterY = y + txtSz.y * 0.5f;
                    
                    // Smart Quote hover logic
                    if (quoteEnabled) {
                        ImVec2 pMin(x, y);
                        ImVec2 pMax(x + fullColW + 5.0f, y + (txtSz.y > 15.0f ? txtSz.y + 8.0f : 18.0f));
                        if (ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(pMin, pMax, false)) {
                            cdl->AddRectFilled(pMin, pMax, IM_COL32(210, 166, 94, 40), 6.0f);
                            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                            if (ImGui::IsMouseReleased(0) && s_canQuote) {
                                ExecuteLawQuote(buildExtendedQuote(sec, law.id, law.txt, law.pun, currentHeader));
                            }
                        }
                    }

                    if (!law.id.empty()) {
                        cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(x, blockCenterY - 6.0f), C_GOLD, law.id.c_str());
                    }
                    cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(x+idW, y), C_WHITE, law.txt.c_str(), nullptr, maxTxtW);
                    if (!law.pun.empty()) {
                        float punX = x + fullColW - punW;
                        float punY = blockCenterY - 6.0f;
                        
                        float lineStartX = x + idW + txtSz.x + 8.0f;
                        float lineEndX = punX - 8.0f;
                        if (lineStartX < lineEndX) {
                            DrawDashedLine(cdl, ImVec2(lineStartX, blockCenterY), ImVec2(lineEndX, blockCenterY), C_LINE, 4, 4);
                        }
                        
                        cdl->AddText(fontSegoeBold14, 12.0f, ImVec2(punX, punY), C_RED, law.pun.c_str());
                    }
                    yOff += (txtSz.y > 15.0f ? txtSz.y + 10.0f : 20.0f);
                }
            };

            ImVec2 cp = ImGui::GetCursorScreenPos();
            float yOff = 0;
            for (int i = 0; i < (int)items.size(); i++) {
                drawItem1col(items[i], cp.x, yOff);
            }
            ImGui::Dummy(ImVec2(0, yOff));
        } else {
            auto& items = sec.items;

            // Split items by col: col=1 (or 0) РІвЂ вЂ™ left, col=2 РІвЂ вЂ™ right
            // This mirrors the launcher layout 1:1
            std::vector<int> col0Indices, col1Indices;
            for (int i = 0; i < (int)items.size(); i++) {
                if (items[i].level == 2) col1Indices.push_back(i);
                else col0Indices.push_back(i);
            }

            // Fallback: if no col=2 items found (old data or parse failure), split in half
            if (col1Indices.empty() && !items.empty()) {
                col0Indices.clear();
                int half = (int)items.size() / 2;
                if (items.size() % 2) half++;
                for (int i = 0; i < half; i++) col0Indices.push_back(i);
                for (int i = half; i < (int)items.size(); i++) col1Indices.push_back(i);
            }

            auto drawItem = [&](const LawItem& law, float x, float& yOff) {
                float y = ImGui::GetCursorScreenPos().y + yOff;
                int itemIdx = (int)(&law - &items[0]);
                if (law.type == "head") {
                    cdl->AddText(fontSegoeBold12, 12.0f, ImVec2(x, y), C_GOLD, law.txt.c_str());
                    cdl->AddLine(ImVec2(x, y+18), ImVec2(x+colW, y+18), C_BORDER, 1.5f);
                    
                    if (quoteEnabled && quoteChapter) {
                        ImVec2 pMin(x, y);
                        ImVec2 pMax(x + colW, y + 18.0f);
                        if (ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(pMin, pMax, false)) {
                            cdl->AddRectFilled(pMin, pMax, IM_COL32(210, 166, 94, 40), 4.0f);
                            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                            if (ImGui::IsMouseReleased(0) && s_canQuote) {
                                ExecuteLawQuote(buildChapterQuote(sec, law.txt));
                            }
                        }
                    }

                    yOff += 30.0f;
                } else if (law.id.empty() && law.txt.empty() && law.pun.empty()) {
                    // Empty placeholder row (spacer) - just add vertical space
                    yOff += 20.0f;
                } else {
                    float idW = law.id.empty() ? 0.0f : (fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, law.id.c_str()).x + 8);
                    float punW = law.pun.empty() ? 0.0f : fontSegoeBold14->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, law.pun.c_str()).x;
                    
                    float maxTxtW = !law.pun.empty() ? (colW - idW - punW - 24.0f) : (colW - idW - 10.0f);
                    if (maxTxtW < 100.0f) maxTxtW = 100.0f;

                    ImVec2 txtSz = fontSegoeBold14->CalcTextSizeA(13.0f, FLT_MAX, maxTxtW, law.txt.c_str());
                    float blockCenterY = y + txtSz.y * 0.5f;
                    
                    // Smart Quote hover logic
                    if (quoteEnabled) {
                        ImVec2 pMin(x, y);
                        ImVec2 pMax(x + colW + 5.0f, y + (txtSz.y > 15.0f ? txtSz.y + 8.0f : 18.0f));
                        if (ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(pMin, pMax, false)) {
                            cdl->AddRectFilled(pMin, pMax, IM_COL32(210, 166, 94, 40), 6.0f);
                            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                            if (ImGui::IsMouseReleased(0) && s_canQuote) {
                                ExecuteLawQuote(buildExtendedQuote(sec, law.id, law.txt, law.pun, getHeaderForIndex(sec, itemIdx)));
                            }
                        }
                    }

                    if (!law.id.empty()) {
                        cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(x, blockCenterY - 6.0f), C_GOLD, law.id.c_str());
                    }
                    cdl->AddText(fontSegoeBold14, 13.0f, ImVec2(x+idW, y), C_WHITE, law.txt.c_str(), nullptr, maxTxtW);
                    if (!law.pun.empty()) {
                        float punX = x + colW - punW;
                        float punY = blockCenterY - 6.0f;
                        
                        float lineStartX = x + idW + txtSz.x + 8.0f;
                        float lineEndX = punX - 8.0f;
                        if (lineStartX < lineEndX) {
                            DrawDashedLine(cdl, ImVec2(lineStartX, blockCenterY), ImVec2(lineEndX, blockCenterY), C_LINE, 4, 4);
                        }
                        
                        cdl->AddText(fontSegoeBold14, 12.0f, ImVec2(punX, punY), C_RED, law.pun.c_str());
                    }

                    yOff += (txtSz.y > 15.0f ? txtSz.y + 10.0f : 20.0f);
                }
            };

            ImVec2 cp = ImGui::GetCursorScreenPos();
            float yOff1 = 0, yOff2 = 0;

            for (int idx : col0Indices) {
                drawItem(items[idx], cp.x, yOff1);
            }
            for (int idx : col1Indices) {
                drawItem(items[idx], cp.x + 340, yOff2);
            }

            float maxY = yOff1 > yOff2 ? yOff1 : yOff2;
            ImGui::Dummy(ImVec2(0, maxY));
        }
    }

    ImGui::EndChild();
    ImGui::PopStyleVar();
    ImGui::PopStyleColor(4);

    // Dropdown button - Rendered AFTER articles so it overlays correctly on top of scroll list text
    float drpX = o.x + 470;
    float drpY = o.y + 65;
    float drpW = 210;
    std::string secName = lawSections.empty() ? "---" : lawSections[selectedLawSection].name;
    float drpH = 30.0f;
    dl->AddRectFilled(ImVec2(drpX, drpY), ImVec2(drpX + drpW, drpY + drpH), C_BOX, 6.0f);
    dl->AddRect(ImVec2(drpX, drpY), ImVec2(drpX + drpW, drpY + drpH), C_GOLD, 6.0f, 0, 1.5f);
    dl->AddText(fontSegoeBold14, 13.0f, ImVec2(drpX+15, drpY+7), C_WHITE, secName.c_str());
    dl->AddLine(ImVec2(drpX+drpW-18, drpY+12), ImVec2(drpX+drpW-12, drpY+18), C_GOLD, 1.5f);
    dl->AddLine(ImVec2(drpX+drpW-12, drpY+18), ImVec2(drpX+drpW-6, drpY+12), C_GOLD, 1.5f);

    if (showLawDropdown && !lawSections.empty()) {
        float ddY = drpY + 36;
        int visibleItems = (int)lawSections.size();
        if (visibleItems > 6) visibleItems = 6;
        float ddH = visibleItems * 36.0f + 10.0f;

        ImGui::SetNextWindowPos(ImVec2(drpX, ddY));
        ImGui::SetNextWindowSize(ImVec2(drpW, ddH));
        
        ImGui::PushStyleColor(ImGuiCol_WindowBg, C_BOX);
        ImGui::PushStyleColor(ImGuiCol_Border, C_LINE);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, 8.0f);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
        ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 1.0f);
        ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(0, 0)); // Strictly 0 to ensure flawless Y-offset math

        // Using a Top-Level Window to cleanly isolate focus for closing when clicked outside
        if (ImGui::Begin("##lawSectionsDropdown", nullptr, ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoScrollbar)) {
            ImDrawList* dcl = ImGui::GetWindowDrawList();
            float itemW = drpW - 12.0f; 

            ImGui::Dummy(ImVec2(0, 4)); // 4px top padding

            for (int i = 0; i < (int)lawSections.size(); i++) {
                ImGui::SetCursorPosX(6.0f);
                ImVec2 p = ImGui::GetCursorScreenPos(); // Exact physical position automatically adjusted for ScrollY
                ImVec2 pMax = ImVec2(p.x + itemW, p.y + 32);
                
                // Raw physical hit-test intersecting strictly the rendered text box, avoiding all logical ImGui bugs
                bool hover = ImGui::IsMouseHoveringRect(p, pMax, true); 
                bool sel = (i == selectedLawSection);
                
                // Draw custom Duran style synced mathematically with the hit-box
                if (sel || hover) {
                    ImU32 bgCol = sel ? C_GOLD_BG : IM_COL32(210, 166, 94, 20); 
                    dcl->AddRectFilled(p, pMax, bgCol, 4.0f);
                    if (sel) dcl->AddRectFilled(p, ImVec2(p.x + 4, p.y + 32), C_GOLD, 2.0f);
                }

                dcl->AddText(fontSegoeBold14, 13.0f, ImVec2(p.x + 9, p.y + 7), (sel || hover) ? C_GOLD : C_WHITE, lawSections[i].name.c_str());

                // Direct click resolution
                if (hover && ImGui::IsMouseClicked(0)) {
                    if (selectedLawSection != i) {
                        selectedLawSection = i;
                        resetLawsScroll = true;
                    }
                    showLawDropdown = false;
                }
                
                // Advance cursor physically for the next loop iteration
                ImGui::Dummy(ImVec2(itemW, 32)); // Box height
                ImGui::Dummy(ImVec2(0, 4));      // Gap beneath box
            }
            
            // Allow close on clicking out
            if (!ImGui::IsWindowFocused() && ImGui::IsMouseClicked(0)) {
                showLawDropdown = false;
            }
            
            ImGui::End();
        }
        ImGui::PopStyleVar(4);
        ImGui::PopStyleColor(2);
    }

    ImGui::SetCursorScreenPos(ImVec2(drpX, drpY));
    if (ImGui::InvisibleButton("##lawDrop", ImVec2(drpW, 30.0f))) {
        showLawDropdown = !showLawDropdown;
    }

}

// ===== Fines Tab ===== SVG: search x=20,y=65, w=390, h=32; list items 390w; right panel x=430 w=250 h=347
void Gui::RenderFinesTab(ImDrawList* dl, ImVec2 o) {
    float cx = o.x + 20, cy = o.y + 65;

    // Empty state
    if (fineItems.empty()) {
        // Disabled search (Matched active measurements)
        dl->AddRectFilled(ImVec2(o.x+20, o.y+65), ImVec2(o.x+460, o.y+95), C_HEADER, 6.0f);
        dl->AddRect(ImVec2(o.x+20, o.y+65), ImVec2(o.x+460, o.y+95), C_BORDER, 6.0f, 0, 1.5f);
        dl->AddCircle(ImVec2(o.x+35, o.y+80), 4.0f, C_GRAY2, 12, 2.0f);
        dl->AddLine(ImVec2(o.x+38, o.y+83), ImVec2(o.x+42, o.y+87), C_GRAY2, 2.0f);
        dl->AddText(fontSegoeBold14, 14.0f, ImVec2(o.x+50, o.y+73), IM_COL32(74,80,89,255),
            "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD0\xBD\xD0\xB5\xD0\xB4\xD0\xBE\xD1\x81\xD1\x82\xD1\x83\xD0\xBF\xD0\xB5\xD0\xBD..."); // РџРѕРёСЃРє РЅРµРґРѕСЃС‚СѓРїРµРЅ...
        
        // Left area: Scales-in-circle icon
        float icx = o.x + 235, icy = o.y + 250;
        dl->AddCircle(ImVec2(icx, icy-20), 30.0f, C_BORDER, 24, 4.0f);
        
        // Scales icon
        dl->AddLine(ImVec2(icx, icy-36), ImVec2(icx, icy-6), C_LINE, 2.5f); // Pillar
        dl->AddLine(ImVec2(icx-8, icy-6), ImVec2(icx+8, icy-6), C_LINE, 2.5f); // Base
        dl->AddLine(ImVec2(icx-18, icy-32), ImVec2(icx+18, icy-32), C_LINE, 2.5f); // Top bar
        // Left bowl
        dl->AddLine(ImVec2(icx-18, icy-32), ImVec2(icx-22, icy-18), C_LINE, 1.5f);
        dl->AddLine(ImVec2(icx-18, icy-32), ImVec2(icx-14, icy-18), C_LINE, 1.5f);
        dl->AddLine(ImVec2(icx-22, icy-18), ImVec2(icx-14, icy-18), C_LINE, 1.5f);
        // Right bowl
        dl->AddLine(ImVec2(icx+18, icy-32), ImVec2(icx+14, icy-18), C_LINE, 1.5f);
        dl->AddLine(ImVec2(icx+18, icy-32), ImVec2(icx+22, icy-18), C_LINE, 1.5f);
        dl->AddLine(ImVec2(icx+14, icy-18), ImVec2(icx+22, icy-18), C_LINE, 1.5f);
        const char* noArt = "\xD0\x9D\xD0\x95\xD0\xA2 \xD0\x94\xD0\x9E\xD0\xA1\xD0\xA2\xD0\xA3\xD0\x9F\xD0\x9D\xD0\xAB\xD0\xA5 \xD0\xA1\xD0\xA2\xD0\x90\xD0\xA2\xD0\x95\xD0\x99"; // РќР•Рў Р”РћРЎРўРЈРџРќР«РҐ РЎРўРђРўР•Р™
        ImVec2 naSz = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, noArt);
        dl->AddText(fontSegoeBold14, 14.0f, ImVec2(icx-naSz.x/2, icy+25), C_GRAY, noArt);
        
        // Right panel (empty state - same size as active: x=470, w=210)
        float px = o.x + 470, py = o.y + 65;
        float panW = 210;
        dl->AddRectFilled(ImVec2(px, py), ImVec2(px+panW, py+345), C_HEADER, 8.0f);
        dl->AddRect(ImVec2(px, py), ImVec2(px+panW, py+345), C_BORDER, 8.0f, 0, 1.5f);
        
        dl->AddText(fontSegoeBold14, 12.0f, ImVec2(px+20, py+14), C_WHITE,
            "\xD0\x98\xD0\xA2\xD0\x9E\xD0\x93\xD0\x9E\xD0\x92\xD0\xAB\xD0\x99 \xD0\xA8\xD0\xA2\xD0\xA0\xD0\x90\xD0\xA4"); // РРўРћР“РћР’Р«Р™ РЁРўР РђР¤
        dl->AddRectFilled(ImVec2(px+20, py+35), ImVec2(px+panW-20, py+37), C_BORDER);
        dl->AddRectFilled(ImVec2(px+20, py+35), ImVec2(px+40, py+37), C_GRAY2);
        
        // Empty message
        const char* emEmpty = "\xD0\xA1\xD0\xBF\xD0\xB8\xD1\x81\xD0\xBE\xD0\xBA \xD0\xBD\xD0\xB0\xD1\x80\xD1\x83\xD1\x88\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB9 \xD0\xBF\xD1\x83\xD1\x81\xD1\x82."; // РЎРїРёСЃРѕРє РЅР°СЂСѓС€РµРЅРёР№ РїСѓСЃС‚.
        ImVec2 e1 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, emEmpty);
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+panW/2-e1.x/2, py+80), IM_COL32(92,99,112,255), emEmpty);
        const char* emClick = "\xD0\x9A\xD0\xBB\xD0\xB8\xD0\xBA\xD0\xBD\xD0\xB8\xD1\x82\xD0\xB5 \xD0\xBF\xD0\xBE \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD1\x8F\xD0\xBC \xD1\x81\xD0\xBB\xD0\xB5\xD0\xB2\xD0\xB0."; // РљР»РёРєРЅРёС‚Рµ РїРѕ СЃС‚Р°С‚СЊСЏРј СЃР»РµРІР°.
        ImVec2 e2 = fontSegoeBold14->CalcTextSizeA(10.0f, FLT_MAX, 0.0f, emClick);
        dl->AddText(fontSegoeBold14, 10.0f, ImVec2(px+panW/2-e2.x/2, py+95), IM_COL32(74,80,89,255), emClick);
        
        // Dashed separator (same position as active: py+150)
        DrawDashedLine(dl, ImVec2(px+20, py+150), ImVec2(px+190, py+150), C_LINE, 4, 4);
        
        // Sum = 0 (positioned same as active state)
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+20, py+165), IM_COL32(92,99,112,255),
            "\xD0\x9E\xD0\x91\xD0\xA9\xD0\x90\xD0\xAF \xD0\xA1\xD0\xA3\xD0\x9C\xD0\x9C\xD0\x90:"); // РћР‘Р©РђРЇ РЎРЈРњРњРђ:
        {
            std::string zeroStr = "0 \xD1\x80\xD1\x83\xD0\xB1.";
            float zeroW = fontSegoeBlack32->CalcTextSizeA(22.0f, FLT_MAX, 0.0f, zeroStr.c_str()).x;
            dl->AddText(fontSegoeBlack32, 22.0f, ImVec2(px+190-zeroW, py+155), C_GRAY, zeroStr.c_str());
        }
        
        // Revoke checkbox disabled (same position as active: py+185)
        float rvY = py + 185;
        dl->AddRectFilled(ImVec2(px+20, rvY), ImVec2(px+190, rvY+30), C_INPUT, 4.0f);
        dl->AddRect(ImVec2(px+20, rvY), ImVec2(px+190, rvY+30), C_BORDER, 4.0f, 0, 1.0f);
        dl->AddRect(ImVec2(px+28, rvY+9), ImVec2(px+40, rvY+21), C_GRAY2, 2.0f, 0, 1.0f);
        dl->AddText(fontSegoeBold12, 9.0f, ImVec2(px+44, rvY+10), IM_COL32(92,99,112,255),
            "\xD0\xA1 \xD0\x98\xD0\x97\xD0\xAA\xD0\xAF\xD0\xA2\xD0\x98\xD0\x95\xD0\x9C \xD0\x92\xD0\x9E\xD0\x94. \xD0\xA3\xD0\x94\xD0\x9E\xD0\xA1\xD0\xA2."); // РЎ РР—РЄРЇРўРР•Рњ Р’РћР”. РЈР”РћРЎРў.
        
        // ID input disabled (same position as active)
        dl->AddText(fontSegoeBold12, 10.0f, ImVec2(px+20, py+226), IM_COL32(92,99,112,255),
            "ID \xD0\x9D\xD0\x90\xD0\xA0\xD0\xA3\xD0\xA8\xD0\x98\xD0\xA2\xD0\x95\xD0\x9B\xD0\xAF"); // ID РќРђР РЈРЁРРўР•Р›РЇ
        dl->AddRectFilled(ImVec2(px+20, py+242), ImVec2(px+190, py+274), C_INPUT, 4.0f);
        dl->AddRect(ImVec2(px+20, py+242), ImVec2(px+190, py+274), C_BORDER, 4.0f, 0, 1.5f);
        dl->AddText(fontSegoeBold14, 12.0f, ImVec2(px+35, py+252), IM_COL32(74,80,89,255), "ID...");
        
        // Buttons disabled (same positions as active: clear 60px, issue 105px)
        float btnY = py + 295;
        dl->AddRectFilled(ImVec2(px+20, btnY), ImVec2(px+80, btnY+34), C_HEADER, 4.0f);
        dl->AddRect(ImVec2(px+20, btnY), ImVec2(px+80, btnY+34), C_BORDER, 4.0f, 0, 1.5f);
        {
            const char* ct = "\xD0\x9E\xD0\xA7\xD0\x98\xD0\xA1\xD0\xA2\xD0\x98\xD0\xA2\xD0\xAC"; // РћР§РРЎРўРРўР¬
            ImVec2 cs = fontSegoeBold12->CalcTextSizeA(10.0f, FLT_MAX, 0.0f, ct);
            dl->AddText(fontSegoeBold12, 10.0f, ImVec2(px+20+(60-cs.x)/2, btnY+(34-cs.y)/2), IM_COL32(92,99,112,255), ct);
        }
        dl->AddRectFilled(ImVec2(px+85, btnY), ImVec2(px+190, btnY+34), C_HEADER, 4.0f);
        dl->AddRect(ImVec2(px+85, btnY), ImVec2(px+190, btnY+34), C_BORDER, 4.0f, 0, 1.5f);
        {
            const char* ft = "\xD0\x92\xD0\xAB\xD0\x9F\xD0\x98\xD0\xA1\xD0\x90\xD0\xA2\xD0\xAC \xD0\xA8\xD0\xA2\xD0\xA0\xD0\x90\xD0\xA4"; // Р’Р«РџРРЎРђРўР¬ РЁРўР РђР¤
            ImVec2 fs = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, ft);
            dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+85+(105-fs.x)/2, btnY+(34-fs.y)/2), IM_COL32(92,99,112,255), ft);
        }
        return;
    }

    // Search bar
    ImGui::SetCursorScreenPos(ImVec2(cx, cy));
    ImGui::PushItemWidth(440);
    ImGui::PushStyleColor(ImGuiCol_FrameBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_Border, C_BORDER);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 6.0f);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 1.5f);
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(30.0f, 8.0f));
    ImGui::PushFont(fontSegoeBold14);
    ImGui::InputTextWithHint("##fineSearch", "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD0\xBD\xD0\xB0\xD1\x80\xD1\x83\xD1\x88\xD0\xB5\xD0\xBD\xD0\xB8\xD1\x8F...", searchFines, 256);
    ImGui::PopFont();
    ImGui::PopStyleVar(3);
    ImGui::PopStyleColor(2);
    ImGui::PopItemWidth();
    // Magnifying glass icon
    dl->AddCircle(ImVec2(cx+15, cy+16), 4.0f, C_GRAY, 12, 2.0f);
    dl->AddLine(ImVec2(cx+18, cy+19), ImVec2(cx+22, cy+23), C_GRAY, 2.0f);

    // Left: violation list - SVG: items 380w, starting at y=110
    float listY = o.y + 110;
    float listH = H - 110 - 15;
    std::string sq = ToLowerUTF8(searchFines);
    float listW = 430;

    ImGui::SetCursorScreenPos(ImVec2(cx, listY));
    ImGui::PushStyleColor(ImGuiCol_ScrollbarBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabHovered, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabActive, C_GOLD);
    ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, 6.0f);
    ImGui::BeginChild("##FinesList", ImVec2(listW + 10, listH), false, ImGuiWindowFlags_NoBackground);
    ImDrawList* cdl = ImGui::GetWindowDrawList();

    ImVec2 cp = ImGui::GetCursorScreenPos();
    float yy = 0;
    for (int i = 0; i < (int)fineItems.size(); i++) {
        auto& fi = fineItems[i];
        if (!sq.empty() && ToLowerUTF8(fi.id).find(sq) == std::string::npos && ToLowerUTF8(fi.name).find(sq) == std::string::npos)
            continue;

        float rx = cp.x, ry = cp.y + yy;

        float revokeBadgeW = fi.hasLicRevoke ? 26.0f : 0.0f;

        std::string amtStr = FormatNumber(fi.amount) + " \xD1\x80\xD1\x83\xD0\xB1.";
        float amtW = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, amtStr.c_str()).x;
        
        float endX = rx + listW - 5.0f - amtW;
        float revokeBadgeX = endX - revokeBadgeW - (revokeBadgeW > 0 ? 10.0f : 0.0f);
        
        float divX = rx + 115.0f;
        float maxNameW = revokeBadgeX - (divX + 10.0f);
        if (maxNameW < 60.0f) maxNameW = 60.0f;
        
        ImVec2 nameSz = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, maxNameW, fi.name.c_str());
        float itemH = nameSz.y > 20.0f ? nameSz.y + 24.0f : 40.0f;

        if (fi.selected) {
            cdl->AddRectFilled(ImVec2(rx, ry), ImVec2(rx+listW, ry+itemH), C_GOLD_BG, 6.0f);
            cdl->AddRect(ImVec2(rx, ry), ImVec2(rx+listW, ry+itemH), C_GOLD, 6.0f, 0, 1.5f);
        } else {
            cdl->AddRectFilled(ImVec2(rx, ry), ImVec2(rx+listW, ry+itemH), C_HEADER, 6.0f);
            cdl->AddRect(ImVec2(rx, ry), ImVec2(rx+listW, ry+itemH), C_BORDER, 6.0f, 0, 1.5f);
        }

        // Checkbox
        float cbx = rx + 15.0f, cby = ry + (itemH - 16.0f) * 0.5f;
        if (fi.selected) {
            cdl->AddRectFilled(ImVec2(cbx, cby), ImVec2(cbx+16, cby+16), C_GOLD, 3.0f);
            cdl->AddLine(ImVec2(cbx+3, cby+8), ImVec2(cbx+6, cby+11), C_DARK, 2.0f);
            cdl->AddLine(ImVec2(cbx+6, cby+11), ImVec2(cbx+13, cby+3), C_DARK, 2.0f);
        } else {
            cdl->AddRect(ImVec2(cbx, cby), ImVec2(cbx+16, cby+16), C_GRAY2, 3.0f, 0, 1.5f);
        }

        // Texts
        float nameY = ry + (itemH > 40.0f ? 12.0f : (itemH - 14.0f) * 0.5f);
        float centerY = ry + (itemH - 14.0f) * 0.5f;
        
        // ID + Type (centered fixed columns)
        float idW = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, fi.id.c_str()).x;
        float idX = (rx + 55.0f) - (idW * 0.5f);
        cdl->AddText(fontSegoeBold14, 14.0f, ImVec2(idX, centerY), C_WHITE, fi.id.c_str());
        
        if (!fi.type.empty()) {
            bool isUK = (fi.type.find("\xD0\xA3\xD0\x9A") != std::string::npos); // "РЈРљ"
            ImU32 fgCol = isUK ? C_RED : C_GOLD;
            float tw = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, fi.type.c_str()).x;
            float typeX = (rx + 92.5f) - (tw * 0.5f);
            cdl->AddText(fontSegoeBold14, 14.0f, ImVec2(typeX, centerY), fgCol, fi.type.c_str());
        }
        
        cdl->AddLine(ImVec2(divX, ry+12), ImVec2(divX, ry+itemH-12), C_LINE, 1.5f);

        cdl->AddText(fontSegoeBold14, 14.0f, ImVec2(divX+8, nameY), C_WHITE, fi.name.c_str(), nullptr, maxNameW);

        // "Р вЂ™Р Р€" badge
        float badgeY = ry + (itemH - 20.0f) * 0.5f;
        if (fi.hasLicRevoke) {
            cdl->AddRectFilled(ImVec2(revokeBadgeX, badgeY), ImVec2(revokeBadgeX+26, badgeY+20), C_RED_BG, 4.0f);
            const char* bTxt = "\xD0\x92\xD0\xA3"; // Р’РЈ uppercase
            float bw = fontSegoeBold12->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, bTxt).x;
            cdl->AddText(fontSegoeBold12, 11.0f, ImVec2(revokeBadgeX + (26-bw)/2, badgeY + 4.0f), C_RED, bTxt);
        }

        // Amount
        float amtY = ry + (itemH - 14.0f) * 0.5f;
        cdl->AddText(fontSegoeBold14, 14.0f, ImVec2(endX, amtY), fi.selected ? C_GOLD : C_GRAY, amtStr.c_str());

        ImGui::SetCursorScreenPos(ImVec2(rx, ry));
        char btnId[32]; sprintf_s(btnId, "##fine%d", i);
        if (ImGui::InvisibleButton(btnId, ImVec2(listW, itemH)))
            fi.selected = !fi.selected;

        yy += itemH + 2.0f;
    }
    ImGui::Dummy(ImVec2(0, 2.0f)); // Small padding at the bottom, no full-height duplicate
    ImGui::EndChild();
    ImGui::PopStyleVar();
    ImGui::PopStyleColor(4);
    // Right: summary panel
    float px = o.x + 470, py = o.y + 65;
    float panW = 210;
    dl->AddRectFilled(ImVec2(px, py), ImVec2(px+panW, py+345), C_HEADER, 8.0f);
    dl->AddRect(ImVec2(px, py), ImVec2(px+panW, py+345), C_BORDER, 8.0f, 0, 1.5f);

    // "Р ВР СћР С›Р вЂњР С›Р вЂ™Р В«Р в„ў Р РЃР СћР В Р С’Р В¤" - SVG: x=20 y=25 font-size=12
    dl->AddText(fontSegoeBold14, 12.0f, ImVec2(px+20, py+14), C_WHITE, "\xD0\x98\xD0\xa2\xD0\x9E\xD0\x93\xD0\x9E\xD0\x92\xD0\xAB\xD0\x99 \xD0\xa8\xD0\xa2\xD0\xa0\xD0\x90\xD0\xa4");
    dl->AddRectFilled(ImVec2(px+20, py+35), ImVec2(px+190, py+37), C_BORDER);
    dl->AddRectFilled(ImVec2(px+20, py+35), ImVec2(px+80, py+37), C_GOLD);

    // Selected items in scrollable region
    int total = 0;
    bool anyRevoke = false;
    ImGui::SetCursorScreenPos(ImVec2(px+20, py+55));
    ImGui::PushStyleColor(ImGuiCol_ScrollbarBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabHovered, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabActive, C_GOLD);
    ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, 6.0f);
    ImGui::BeginChild("##SelectedFines", ImVec2(170, 80), false, ImGuiWindowFlags_NoBackground);
    ImDrawList* scl = ImGui::GetWindowDrawList();
    ImVec2 sfp = ImGui::GetCursorScreenPos();
    float sy = 0;
    for (auto& fi : fineItems) {
        if (!fi.selected) continue;
        total += fi.amount;
        if (fi.hasLicRevoke) anyRevoke = true;
        scl->AddText(fontSegoeBold14, 11.0f, ImVec2(sfp.x, sfp.y + sy), C_GOLD, fi.id.c_str());
        float idW2 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, fi.id.c_str()).x;
        
        std::string a2 = FormatNumber(fi.amount);
        float a2W = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, a2.c_str()).x;
        
        float maxName2W = 160.0f - idW2 - 10.0f - a2W - 10.0f;
        if (maxName2W < 40.0f) maxName2W = 40.0f;
        
        ImVec2 nameSz2 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, maxName2W, fi.name.c_str());
        
        scl->AddText(fontSegoeBold14, 11.0f, ImVec2(sfp.x+idW2+8, sfp.y + sy), C_WHITE, fi.name.c_str(), nullptr, maxName2W);
        scl->AddText(fontSegoeBold14, 11.0f, ImVec2(sfp.x+160-a2W, sfp.y + sy), C_WHITE, a2.c_str());
        
        sy += (nameSz2.y > 15.0f ? nameSz2.y + 6.0f : 20.0f);
    }
    ImGui::Dummy(ImVec2(0, sy > 0 ? sy : 1));
    ImGui::EndChild();
    ImGui::PopStyleVar();
    ImGui::PopStyleColor(4);
    
    if (sy == 0) {
        float emptyCenterY = py + 90; // Slightly lower
        float iconRadius = 20.0f; // Slightly smaller to prevent overlap
        
        // Draw background circle
        dl->AddCircleFilled(ImVec2(px + panW / 2, emptyCenterY - 18), iconRadius, C_INPUT);
        dl->AddCircle(ImVec2(px + panW / 2, emptyCenterY - 18), iconRadius, C_BORDER, 24, 1.5f);
        
        // Draw document/fine icon
        float ix = px + panW / 2;
        float iy = emptyCenterY - 18;
        ImU32 iconC = IM_COL32(120, 125, 135, 255);
        dl->AddRect(ImVec2(ix - 7, iy - 9), ImVec2(ix + 7, iy + 9), iconC, 2.0f, 0, 1.5f); // Paper
        dl->AddLine(ImVec2(ix - 3, iy - 4), ImVec2(ix + 3, iy - 4), iconC, 1.5f); // Line 1
        dl->AddLine(ImVec2(ix - 3, iy), ImVec2(ix + 3, iy), iconC, 1.5f); // Line 2
        dl->AddLine(ImVec2(ix - 3, iy + 4), ImVec2(ix + 1, iy + 4), iconC, 1.5f); // Line 3
        dl->AddCircleFilled(ImVec2(ix + 7, iy + 9), 5.0f, C_HEADER); // Cutout for badge
        dl->AddCircleFilled(ImVec2(ix + 7, iy + 9), 3.5f, C_GOLD); // Gold badge (warning)
        
        const char* emClick1 = "\xD0\xA1\xD0\xBF\xD0\xB8\xD1\x81\xD0\xBE\xD0\xBA \xD0\xB2\xD1\x8B\xD0\xB4\xD0\xB0\xD1\x87\xD0\xB8 \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84\xD0\xB0 \xD0\xBF\xD1\x83\xD1\x81\xD1\x82."; // РЎРїРёСЃРѕРє РІС‹РґР°С‡Рё С€С‚СЂР°С„Р° РїСѓСЃС‚.
        const char* emClick2 = "\xD0\x9A\xD0\xBB\xD0\xB8\xD0\xBA\xD0\xBD\xD0\xB8\xD1\x82\xD0\xB5 \xD0\xBF\xD0\xBE \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD1\x8F\xD0\xBC \xD1\x81\xD0\xBB\xD0\xB5\xD0\xB2\xD0\xB0,"; // РљР»РёРєРЅРёС‚Рµ РїРѕ СЃС‚Р°С‚СЊСЏРј СЃР»РµРІР°,
        const char* emClick3 = "\xD1\x87\xD1\x82\xD0\xBE\xD0\xB1\xD1\x8B \xD0\xB4\xD0\xBE\xD0\xB1\xD0\xB0\xD0\xB2\xD0\xB8\xD1\x82\xD1\x8C \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84."; // РЎвЂЎРЎвЂљР С•Р В±РЎвЂ№ Р Т‘Р С•Р В±Р В°Р Р†Р С‘РЎвЂљРЎРЉ РЎв‚¬РЎвЂљРЎР‚Р В°РЎвЂћ.
        
        ImVec2 e2_1 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, emClick1);
        ImVec2 e2_2 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, emClick2);
        ImVec2 e2_3 = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, emClick3);
        
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+panW/2-e2_1.x/2, emptyCenterY + 11), IM_COL32(92,99,112,255), emClick1);
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+panW/2-e2_2.x/2, emptyCenterY + 26), IM_COL32(74,80,89,255), emClick2);
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+panW/2-e2_3.x/2, emptyCenterY + 39), IM_COL32(74,80,89,255), emClick3);
    }

    // Dashed separator
    DrawDashedLine(dl, ImVec2(px+20, py+150), ImVec2(px+190, py+150), C_LINE, 4, 4);

    // "Р С›Р вЂР В©Р С’Р Р‡ Р РЋР Р€Р СљР СљР С’:" and total on the same visual line
    dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+20, py+165), C_GRAY, "\xD0\x9E\xD0\x91\xD0\xa9\xD0\x90\xD0\xAF \xD0\xa1\xD0\xa3\xD0\x9C\xD0\x9C\xD0\x90:");
    std::string totalStr = FormatNumber(total) + " \xD1\x80\xD1\x83\xD0\xB1.";
    float totalW = fontSegoeBlack32->CalcTextSizeA(22.0f, FLT_MAX, 0.0f, totalStr.c_str()).x;
    dl->AddText(fontSegoeBlack32, 22.0f, ImVec2(px+190-totalW, py+155), C_GOLD, totalStr.c_str());

    // "Р РЋ Р ВР вЂ”Р Р„Р Р‡Р СћР ВР вЂўР Сљ Р вЂ™Р С›Р вЂќ. Р Р€Р вЂќР С›Р РЋР СћР С›Р вЂ™Р вЂўР В Р вЂўР СњР ВР Р‡"
    float rvY = py + 185;
    bool canRevoke = anyRevoke; // enabled if at least one selected fine has revoke
    bool isRevoked = canRevoke && fineWithRevoke;
    ImU32 rvCol = canRevoke ? (isRevoked ? C_RED : C_GOLD) : C_GRAY2;
    ImU32 rvBgCol = canRevoke ? (isRevoked ? C_RED_BG : IM_COL32(0,0,0,0)) : C_INPUT;
    
    dl->AddRectFilled(ImVec2(px+20, rvY), ImVec2(px+190, rvY+30), rvBgCol, 4.0f);
    dl->AddRect(ImVec2(px+20, rvY), ImVec2(px+190, rvY+30), rvCol, 4.0f, 0, 1.0f);
    
    if (isRevoked) {
        dl->AddRectFilled(ImVec2(px+28, rvY+9), ImVec2(px+40, rvY+21), C_RED, 2.0f);
        dl->AddLine(ImVec2(px+30, rvY+15), ImVec2(px+33, rvY+18), C_DARK, 1.5f);
        dl->AddLine(ImVec2(px+33, rvY+18), ImVec2(px+38, rvY+11), C_DARK, 1.5f);
    } else {
        dl->AddRect(ImVec2(px+28, rvY+9), ImVec2(px+40, rvY+21), canRevoke ? C_LINE : C_GRAY2, 2.0f, 0, 1.0f);
    }
    
    // Allow toggle of revoke checkbox physically
    ImGui::SetCursorScreenPos(ImVec2(px+20, rvY));
    if (ImGui::InvisibleButton("##toggleRevoke", ImVec2(170, 30))) {
        if (canRevoke) {
            fineWithRevoke = !fineWithRevoke;
        }
    }
    
    dl->AddText(fontSegoeBold12, 9.0f, ImVec2(px+44, rvY+10), isRevoked ? C_RED : (canRevoke ? C_GOLD : C_GRAY2),
        "\xD0\xa1 \xD0\x98\xD0\x97\xD0\xAA\xD0\xAF\xD0\xa2\xD0\x98\xD0\x95\xD0\x9C \xD0\x92\xD0\x9E\xD0\x94. \xD0\xa3\xD0\x94\xD0\x9E\xD0\xa1\xD0\xa2.");

    // ID input
    dl->AddText(fontSegoeBold12, 10.0f, ImVec2(px+20, py+226), C_GRAY, "ID \xD0\x9D\xD0\x90\xD0\xa0\xD0\xa3\xD0\xa8\xD0\x98\xD0\xa2\xD0\x95\xD0\x9B\xD0\xAF");
    ImGui::SetCursorScreenPos(ImVec2(px+20, py+242));
    ImGui::PushItemWidth(170);
    ImGui::PushStyleColor(ImGuiCol_FrameBg, C_INPUT);
    ImGui::PushStyleColor(ImGuiCol_TextDisabled, IM_COL32(100,110,120,255));
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 4.0f);
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(10.0f, 6.0f));
    ImGui::PushFont(fontSegoeBold14);
    ImGui::InputTextWithHint("##fineId", "\xD0\x92\xD0\xB2\xD0\xB5\xD0\xB4\xD0\xB8\xD1\x82\xD0\xB5 ID \xD0\xBD\xD0\xB0\xD1\x80\xD1\x83\xD1\x88\xD0\xB8\xD1\x82\xD0\xB5\xD0\xBB\xD1\x8F", fineIdBuf, 32);
    ImGui::PopFont();
    ImGui::PopStyleVar(2);
    ImGui::PopStyleColor(2);
    ImGui::PopItemWidth();

    // Buttons at bottom
    float btnY = py + 295;
    // "Р С›Р В§Р ВР РЋР СћР ВР СћР В¬" (clear) w=60 h=34
    ImGui::SetCursorScreenPos(ImVec2(px+20, btnY));
    bool clrClicked = ImGui::InvisibleButton("##clearFines", ImVec2(60, 34));
    bool clrHover = ImGui::IsItemHovered();
    
    dl->AddRectFilled(ImVec2(px+20, btnY), ImVec2(px+80, btnY+34), clrHover ? IM_COL32(255,255,255,10) : C_BOX, 4.0f);
    dl->AddRect(ImVec2(px+20, btnY), ImVec2(px+80, btnY+34), C_LINE, 4.0f, 0, 1.5f);
    {
        const char* clrTxt = "\xD0\x9E\xD0\xa7\xD0\x98\xD0\xa1\xD0\xa2\xD0\x98\xD0\xa2\xD0\xAC";
        ImVec2 clrSz = fontSegoeBold12->CalcTextSizeA(10.0f, FLT_MAX, 0.0f, clrTxt);
        dl->AddText(fontSegoeBold12, 10.0f, ImVec2(px+20+(60-clrSz.x)/2, btnY+(34-clrSz.y)/2), clrHover ? C_WHITE : C_GRAY, clrTxt);
    }
    if (clrClicked) {
        for (auto& fi : fineItems) fi.selected = false;
        memset(fineIdBuf, 0, sizeof(fineIdBuf));
        fineWithRevoke = false;
    }

    // "Р вЂ™Р В«Р СџР ВР РЋР С’Р СћР В¬ Р РЃР СћР В Р С’Р В¤" (issue fine) w=105 h=34
    ImGui::SetCursorScreenPos(ImVec2(px+85, btnY));
    bool issClicked = ImGui::InvisibleButton("##issueFine", ImVec2(105, 34));
    bool issHover = ImGui::IsItemHovered();
    
    dl->AddRectFilled(ImVec2(px+85, btnY), ImVec2(px+190, btnY+34), issHover ? IM_COL32(63,185,80,255) : C_GREEN, 4.0f);
    {
        const char* issTxt = "\xD0\x92\xD0\xAB\xD0\x9F\xD0\x98\xD0\xa1\xD0\x90\xD0\xa2\xD0\xAC \xD0\xa8\xD0\xa2\xD0\xa0\xD0\x90\xD0\xA4";
        ImVec2 issSz = fontSegoeBold14->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, issTxt);
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(px+85+(105-issSz.x)/2, btnY+(34-issSz.y)/2), C_DARK, issTxt);
    }
    if (issClicked) {
        // --- Issue Fine Logic ---
        std::string idStr(fineIdBuf);
        // Trim spaces
        while (!idStr.empty() && idStr.front() == ' ') idStr.erase(idStr.begin());
        while (!idStr.empty() && idStr.back() == ' ') idStr.pop_back();
        
        if (idStr.empty()) {
            ShowError("\xD0\x92\xD0\xB2\xD0\xB5\xD0\xB4\xD0\xB8\xD1\x82\xD0\xB5 ID \xD0\xBD\xD0\xB0\xD1\x80\xD1\x83\xD1\x88\xD0\xB8\xD1\x82\xD0\xB5\xD0\xBB\xD1\x8F!", C_RED); // Р’РІРµРґРёС‚Рµ ID РЅР°СЂСѓС€РёС‚РµР»СЏ!
        } else {
            // Check for any selected fines
            bool hasSelected = false;
            int totalAmt = 0;
            
            // Group articles by type for formatting
            std::map<std::string, std::vector<std::string>> groupedArticles;
            for (auto& fi : fineItems) {
                if (!fi.selected) continue;
                hasSelected = true;
                totalAmt += fi.amount;
                // Add to list for this type
                groupedArticles[fi.type].push_back(fi.id);
            }
            
            if (!hasSelected) {
                ShowError("\xD0\x92\xD1\x8B\xD0\xB1\xD0\xB5\xD1\x80\xD0\xB8\xD1\x82\xD0\xB5 \xD1\x85\xD0\xBE\xD1\x82\xD1\x8F \xD0\xB1\xD1\x8B \xD0\xBE\xD0\xB4\xD0\xBD\xD0\xBE \xD0\xBD\xD0\xB0\xD1\x80\xD1\x83\xD1\x88\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB5!", C_RED); // Р’С‹Р±РµСЂРёС‚Рµ С…РѕС‚СЏ Р±С‹ РѕРґРЅРѕ РЅР°СЂСѓС€РµРЅРёРµ!
            } else {
                // Build article string: ID1, ID2 TYPE1, ID3 TYPE2
                std::string articlesStr;
                for (auto const& [type, ids] : groupedArticles) {
                    if (!articlesStr.empty()) articlesStr += ", ";
                    for (size_t i = 0; i < ids.size(); i++) {
                        articlesStr += ids[i];
                        if (i < ids.size() - 1) articlesStr += ", ";
                    }
                    articlesStr += " " + type;
                }

                if (totalAmt > 25000) totalAmt = 25000;

                std::string cmd = "/ticket " + idStr + " " + std::to_string(totalAmt) + " " + articlesStr;
                std::string cp1251cmd = UTF8ToCP1251(cmd.c_str());
                
                Toggle();
                RunOnMainThread([cp1251cmd]() {
                    SendSAMPMessage(cp1251cmd.c_str());
                });
                
                // Build quote text if quoting is enabled
                std::string quoteStr;
                if (quoteFines) {
                    quoteStr = "\xD0\x92\xD1\x8B\xD0\xBF\xD0\xB8\xD1\x81\xD0\xB0\xD0\xBD \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84 \xD0\xBF\xD0\xBE:\n";
                    for (auto& fi : fineItems) {
                        if (!fi.selected) continue;
                        quoteStr += fi.id + " \"" + fi.type + "\"\n";
                        quoteStr += fi.name + "\n";
                    }
                    quoteStr += "\xD0\x98\xD1\x82\xD0\xBE\xD0\xB3\xD0\xBE\xD0\xB2\xD0\xB0\xD1\x8F \xD1\x81\xD1\x83\xD0\xBC\xD0\xBC\xD0\xB0: " + FormatNumber(totalAmt) + " \xD1\x80\xD1\x83\xD0\xB1.";
                }
                
                // Start unified sequence (revoke + quote via /go /stop)
                bool needRevoke = fineWithRevoke;
                bool needQuote = quoteFines;
                StartFineSequence(idStr, articlesStr, needRevoke, needQuote, quoteStr);
                
                // Clear state after issuing successfully
                for (auto& fi : fineItems) fi.selected = false;
                memset(fineIdBuf, 0, sizeof(fineIdBuf));
                fineWithRevoke = false;
            }
        }
    }
}

// ===== Binder Tab ===== SVG: sidebar x=20,y=65 w=140 h=347; search x=180,y=65 w=500 h=32; cards 240wР“вЂ”45h
void Gui::RenderBinderTab(ImDrawList* dl, ImVec2 o) {
    // Left sidebar - SVG: translate(20,65) w=140 h=347 rx=8
    float sx = o.x + 20, sy = o.y + 65;
    float sbW = 140, sbH = 347;
    dl->AddRectFilled(ImVec2(sx, sy), ImVec2(sx+sbW, sy+sbH), C_HEADER, 8.0f);
    dl->AddRect(ImVec2(sx, sy), ImVec2(sx+sbW, sy+sbH), C_BORDER, 8.0f, 0, 1.5f);

    // "Р вЂњР В Р Р€Р СџР СџР В« Р вЂР ВР СњР вЂќР С›Р вЂ™" - SVG: font-size=10 font-weight=900 letter-spacing=1
    dl->AddText(fontSegoeBold14, 12.0f, ImVec2(sx+12, sy+14), C_GRAY,
        "\xD0\x93\xD0\xa0\xD0\xa3\xD0\x9F\xD0\x9F\xD0\xAB \xD0\x91\xD0\x98\xD0\x9D\xD0\x94\xD0\x9E\xD0\x92");
    dl->AddLine(ImVec2(sx+10, sy+40), ImVec2(sx+130, sy+40), C_BORDER, 1.5f);

    auto& binds = BinderManager::Get().Binds;
    
    // Empty state
    if (binds.empty()) {
        // Sidebar: folder icon + "Р СњР ВµРЎвЂљ Р С–РЎР‚РЎС“Р С—Р С—"
        float fgx = sx + (sbW / 2.0f), fgy = sy + 160;
        dl->AddRectFilled(ImVec2(fgx-15, fgy-10), ImVec2(fgx+15, fgy+10), C_LINE, 2.0f);
        dl->AddLine(ImVec2(fgx-15, fgy-6), ImVec2(fgx+15, fgy-6), C_LINE, 2.0f);
        const char* ngText = "\xD0\x9D\xD0\xB5\xD1\x82 \xD0\xB3\xD1\x80\xD1\x83\xD0\xBF\xD0\xBF";
        ImVec2 ngSz = fontSegoeBold12->CalcTextSizeA(10.0f, FLT_MAX, 0.0f, ngText);
        dl->AddText(fontSegoeBold12, 10.0f, ImVec2(fgx - ngSz.x/2, fgy+20), IM_COL32(92,99,112,255), ngText);
        
        float rx = o.x + 180, ry = o.y + 65;
        dl->AddRectFilled(ImVec2(rx, ry), ImVec2(rx+500, ry+32), C_HEADER, 6.0f);
        dl->AddRect(ImVec2(rx, ry), ImVec2(rx+500, ry+32), C_BORDER, 6.0f, 0, 1.5f);
        dl->AddCircle(ImVec2(rx+15, ry+16), 4.0f, C_GRAY2, 12, 2.0f);
        dl->AddLine(ImVec2(rx+18, ry+19), ImVec2(rx+22, ry+23), C_GRAY2, 2.0f);
        dl->AddText(fontSegoeBold14, 13.0f, ImVec2(rx+30, ry+9), IM_COL32(74,80,89,255),
            "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD0\xBD\xD0\xB5\xD0\xB4\xD0\xBE\xD1\x81\xD1\x82\xD1\x83\xD0\xBF\xD0\xB5\xD0\xBD..."); // РџРѕРёСЃРє РЅРµРґРѕСЃС‚СѓРїРµРЅ...
        
        // Center: grid icon (4 small squares + bar)
        float gcx = rx + 245, gcy = ry + 170;
        dl->AddRectFilled(ImVec2(gcx-50, gcy-30), ImVec2(gcx+50, gcy+30), IM_COL32(0,0,0,0)); // transparent
        dl->AddRect(ImVec2(gcx-50, gcy-30), ImVec2(gcx+50, gcy+30), C_BORDER, 6.0f, 0, 4.0f);
        dl->AddRectFilled(ImVec2(gcx-35, gcy-15), ImVec2(gcx-20, gcy), C_LINE, 2.0f);
        dl->AddRectFilled(ImVec2(gcx-15, gcy-15), ImVec2(gcx, gcy), C_LINE, 2.0f);
        dl->AddRectFilled(ImVec2(gcx+5, gcy-15), ImVec2(gcx+20, gcy), C_LINE, 2.0f);
        dl->AddRectFilled(ImVec2(gcx+25, gcy-15), ImVec2(gcx+40, gcy), C_LINE, 2.0f);
        dl->AddRectFilled(ImVec2(gcx-15, gcy+5), ImVec2(gcx+40, gcy+15), C_LINE, 2.0f);
        
        // Empty state match for Binder
        const char* noBinds = "\xD0\x91\xD0\x98\xD0\x9D\xD0\x94\xD0\xAB \xD0\x9D\xD0\x95 \xD0\x9D\xD0\x90\xD0\xA1\xD0\xA2\xD0\xA0\xD0\x9E\xD0\x95\xD0\x9D\xD0\xAB"; // Р‘РРќР”Р« РќР• РќРђРЎРўР РћР•РќР«
        ImVec2 nb = fontSegoeBold20->CalcTextSizeA(16.0f, FLT_MAX, 0.0f, noBinds);
        dl->AddText(fontSegoeBold20, 16.0f, ImVec2(gcx-nb.x/2, gcy+55), C_GRAY, noBinds);
        
        const char* sub = "\xD0\xA1\xD0\xBE\xD0\xB7\xD0\xB4\xD0\xB0\xD0\xB9\xD1\x82\xD0\xB5 \xD0\xBF\xD1\x80\xD0\xBE\xD1\x84\xD0\xB8\xD0\xBB\xD1\x8C \xD0\xB8 \xD0\xB4\xD0\xBE\xD0\xB1\xD0\xB0\xD0\xB2\xD1\x8C\xD1\x82\xD0\xB5 \xD0\xB1\xD0\xB8\xD0\xBD\xD0\xB4\xD1\x8B \xD0\xB2 \xD0\xBB\xD0\xB0\xD1\x83\xD0\xBD\xD1\x87\xD0\xB5\xD1\x80\xD0\xB5."; // РЎРѕР·РґР°Р№С‚Рµ РїСЂРѕС„РёР»СЊ Рё РґРѕР±Р°РІСЊС‚Рµ Р±РёРЅРґС‹ РІ Р»Р°СѓРЅС‡РµСЂРµ.
        ImVec2 sb2 = fontSegoeBold14->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, sub);
        dl->AddText(fontSegoeBold14, 12.0f, ImVec2(gcx-sb2.x/2, gcy+75), IM_COL32(92,99,112,255), sub);
        return;
    }
    std::vector<std::string> groups;
    for (auto& b : binds) {
        if (b.name == "Radial Menu") continue;
        if (!b.Group.empty()) {
            std::string lGroup = ToLowerUTF8(b.Group);
            if (lGroup == "\xd0\xb2\xd1\x81\xd0\xb5" || lGroup == "all") continue; // Skip "Р’СЃРµ" or "Р’РЎР•"
            
            bool found = false;
            for (auto& g : groups) {
                if (ToLowerUTF8(g) == lGroup) { found = true; break; }
            }
            if (!found) groups.push_back(b.Group);
        }
    }

    // "Р вЂ™РЎРѓР Вµ Р В±Р С‘Р Р…Р Т‘РЎвЂ№ (N)" - SVG: x=10 y=55 w=120 h=26
    float gy = sy + 50;
    bool allSel = (selectedBindGroup == -1);
    char allLabel[64];
    sprintf_s(allLabel, "\xD0\x92\xD1\x81\xD0\xB5 \xD0\xB1\xD0\xB8\xD0\xBD\xD0\xB4\xD1\x8B (%d)", (int)binds.size());
    if (allSel) {
        dl->AddRectFilled(ImVec2(sx+10, gy), ImVec2(sx+130, gy+28), C_GOLD_BG, 6.0f);
        dl->AddRectFilled(ImVec2(sx+10, gy), ImVec2(sx+12, gy+28), C_GOLD, 2.0f);
    }
    dl->AddText(fontSegoeBold14, 11.0f, ImVec2(sx+20, gy+6), allSel ? C_GOLD : C_WHITE, allLabel);
    ImGui::SetCursorScreenPos(ImVec2(sx+10, gy));
    if (ImGui::InvisibleButton("##grpAll", ImVec2(120, 28))) selectedBindGroup = -1;
    gy += 32;

    for (int gi = 0; gi < (int)groups.size(); gi++) {
        int count = 0;
        std::string lTarget = ToLowerUTF8(groups[gi]);
        for (auto& b : binds) if (ToLowerUTF8(b.Group) == lTarget) count++;
        
        bool gSel = (selectedBindGroup == gi);
        if (gSel) {
            dl->AddRectFilled(ImVec2(sx+10, gy), ImVec2(sx+130, gy+28), C_GOLD_BG, 6.0f);
            dl->AddRectFilled(ImVec2(sx+10, gy), ImVec2(sx+12, gy+28), C_GOLD, 2.0f);
        }
        
        char grpLabel[128];
        sprintf_s(grpLabel, "%s (%d)", groups[gi].c_str(), count);
        dl->AddText(fontSegoeBold14, 11.0f, ImVec2(sx+20, gy+6), gSel ? C_GOLD : C_WHITE, grpLabel);
        
        ImGui::SetCursorScreenPos(ImVec2(sx+10, gy));
        char gid[32]; sprintf_s(gid, "##grp%d", gi);
        if (ImGui::InvisibleButton(gid, ImVec2(120, 28))) selectedBindGroup = gi;
        gy += 32;
    }

    // Right: search + cards - SVG: translate(180,65), search w=500 h=32
    float rx = o.x + 180, ry = o.y + 65;
    float rw = 500;

    // Search bar - SVG: w=500 h=32 rx=6
    ImGui::SetCursorScreenPos(ImVec2(rx, ry));
    ImGui::PushItemWidth(rw);
    ImGui::PushStyleColor(ImGuiCol_FrameBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_Border, C_BORDER);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 6.0f);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 1.5f);
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(30.0f, 8.0f));
    ImGui::PushFont(fontSegoeBold14);
    ImGui::InputTextWithHint("##bindSearch",
        "\xD0\x9F\xD0\xBe\xD0\xB8\xD1\x81\xD0\xBA \xD0\xB1\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB0...", searchBinder, 256);
    ImGui::PopFont();
    ImGui::PopStyleVar(3);
    ImGui::PopStyleColor(2);
    ImGui::PopItemWidth();
    // Magnifying glass icon
    dl->AddCircle(ImVec2(rx+15, ry+16), 4.0f, C_GRAY, 12, 2.0f);
    dl->AddLine(ImVec2(rx+18, ry+19), ImVec2(rx+22, ry+23), C_GRAY, 2.0f);

    // Cards area - SVG: cards 235wР“вЂ”45h, two columns, 10px gap, starting at y=45 from search
    float cardsY = ry + 45;
    float cardW = 235;
    float cardH = 45;
    std::string bsq = ToLowerUTF8(searchBinder);

    ImGui::SetCursorScreenPos(ImVec2(rx, cardsY));
    ImGui::PushStyleColor(ImGuiCol_ScrollbarBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabHovered, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabActive, C_GOLD);
    ImGui::BeginChild("##BindCards", ImVec2(rw, sbH - 50), false, ImGuiWindowFlags_NoBackground | ImGuiWindowFlags_AlwaysVerticalScrollbar);
    ImDrawList* cdlb = ImGui::GetWindowDrawList();

    ImVec2 bp = ImGui::GetCursorScreenPos();
    int col = 0;
    float bx = 0, by = 0;

    for (int i = 0; i < (int)binds.size(); i++) {
        auto& b = binds[i];

        if (b.name == "Radial Menu") continue;

        if (selectedBindGroup >= 0 && selectedBindGroup < (int)groups.size()) {
            if (b.Group != groups[selectedBindGroup]) continue;
        }
        if (!bsq.empty() && ToLowerUTF8(b.name).find(bsq) == std::string::npos && ToLowerUTF8(b.KeyComboStr()).find(bsq) == std::string::npos)
            continue;

        float cx2 = bp.x + bx, cy2 = bp.y + by;
        bool hover = ImGui::IsMouseHoveringRect(ImVec2(cx2, cy2), ImVec2(cx2+cardW, cy2+cardH));

        ImU32 borderColor = (b.isAuto || hover) ? C_GOLD : C_BORDER;
        ImU32 headColor = b.isAuto ? C_GOLD_BG : C_HEADER;
        
        cdlb->AddRectFilled(ImVec2(cx2, cy2), ImVec2(cx2+cardW, cy2+cardH), headColor, 8.0f);
        cdlb->AddRect(ImVec2(cx2, cy2), ImVec2(cx2+cardW, cy2+cardH), borderColor, 8.0f, 0, 1.5f);

        // Key badge - SVG: x=10 y=10 w=50 h=24 rx=4
        float kbW = 0.0f;
        if (b.isAuto) {
            kbW = 50.0f;
            // Gold logic for auto binds
            cdlb->AddRectFilled(ImVec2(cx2+10, cy2+10), ImVec2(cx2+10+kbW, cy2+34), C_GOLD, 4.0f);
            
            // Draw auto-replace icon (lightning bolt) inside badge centered
            float acx = cx2+10 + kbW/2 + 0.5f, acy = cy2+22 - 0.5f;
            cdlb->AddQuadFilled(ImVec2(acx-1, acy-6), ImVec2(acx+4, acy-6), ImVec2(acx+1, acy+1), ImVec2(acx-4, acy+1), C_DARK);
            cdlb->AddTriangleFilled(ImVec2(acx-2, acy+1), ImVec2(acx+3, acy+1), ImVec2(acx-1, acy+7), C_DARK);
        } else {
            std::string keyStr = b.KeyComboStr();
            if (keyStr.empty()) keyStr = "\xD0\x9D\xD0\x95\xD0\xa2";
            float tw = fontSegoeBold12->CalcTextSizeA(10.0f, FLT_MAX, 0.0f, keyStr.c_str()).x;
            kbW = tw + 14.0f;
            if (kbW < 50.0f) kbW = 50.0f;
            
            bool hasKey = !keyStr.empty() && keyStr != "\xD0\x9D\xD0\x95\xD0\xa2";
            if (hasKey && hover) {
                cdlb->AddRectFilled(ImVec2(cx2+10, cy2+10), ImVec2(cx2+10+kbW, cy2+34), C_GOLD, 4.0f);
                cdlb->AddText(fontSegoeBold12, 10.0f, ImVec2(cx2+10+(kbW-tw)/2, cy2+15), C_DARK, keyStr.c_str());
            } else {
                cdlb->AddRectFilled(ImVec2(cx2+10, cy2+10), ImVec2(cx2+10+kbW, cy2+34), C_BORDER, 4.0f);
                cdlb->AddRect(ImVec2(cx2+10, cy2+10), ImVec2(cx2+10+kbW, cy2+34), C_LINE, 4.0f);
                cdlb->AddText(fontSegoeBold12, 10.0f, ImVec2(cx2+10+(kbW-tw)/2, cy2+15),
                    hasKey ? C_GOLD : C_GRAY, keyStr.c_str());
            }
        }

        // Name - SVG: x=70 y=27 font-size=12
        cdlb->AddText(fontSegoeBold14, 12.0f, ImVec2(cx2+10+kbW+10, cy2+14), hover ? C_GOLD : C_WHITE, b.name.c_str());

        // Play icon - clean triangle with circle
        float px2 = cx2 + cardW - 25, py2 = cy2 + cardH/2;
        ImU32 playCol = hover ? C_GOLD : C_GRAY2;
        cdlb->AddCircleFilled(ImVec2(px2, py2), 9.0f, hover ? IM_COL32(210,166,94,40) : IM_COL32(40,44,52,255), 24);
        cdlb->AddCircle(ImVec2(px2, py2), 9.0f, playCol, 24, 1.5f);
        // Smooth triangle (play arrow) РІР‚вЂќ compact, with 3px padding from circle edge
        ImVec2 triPts[3] = {
            ImVec2(px2-2.5f, py2-4),
            ImVec2(px2-2.5f, py2+4),
            ImVec2(px2+4, py2)
        };
        cdlb->AddTriangleFilled(triPts[0], triPts[1], triPts[2], playCol);

        ImGui::SetCursorScreenPos(ImVec2(px2-8, py2-10));
        char pid[32]; sprintf_s(pid, "##play%d", i);
        if (ImGui::InvisibleButton(pid, ImVec2(20, 20))) {
            Toggle();
            BinderManager::Get().ExecuteBind(b);
        }

        col++;
        if (col >= 2) { col = 0; bx = 0; by += cardH + 10; }
        else { bx = cardW + 10; }
    }
    if (col == 1) by += cardH + 10;
    ImGui::Dummy(ImVec2(0, 15)); // bottom padding so last card isn't clipped
    ImGui::EndChild();
    ImGui::PopStyleColor(4);
}

// ===== Settings Tab ===== SVG: settingstop.svg
void Gui::RenderSettingsTab(ImDrawList* parent_dl, ImVec2 origin) {
    ImGui::SetCursorScreenPos(ImVec2(origin.x + 20, origin.y + 65));
    
    // Custom scrollbar for Settings
    ImGui::PushStyleColor(ImGuiCol_ScrollbarBg, C_HEADER);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabHovered, C_GOLD);
    ImGui::PushStyleColor(ImGuiCol_ScrollbarGrabActive, C_GOLD);
    ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, 6.0f);
    
    ImGui::BeginChild("##SettingsScroll", ImVec2(W - 26, H - 65 - 15), false, ImGuiWindowFlags_NoBackground);
    ImDrawList* dl = ImGui::GetWindowDrawList();
    ImVec2 o = ImGui::GetCursorScreenPos();
    o.x -= 20; // Revert offset so original coordinates work
    o.y -= 65; 

    float sx = o.x + 20, sy = o.y + 65;
    
    // Left panel: VISUAL SETTINGS - x=20, y=65, w=320, h=290
    dl->AddRectFilled(ImVec2(sx, sy), ImVec2(sx+320, sy+290), C_HEADER, 8.0f);
    dl->AddRect(ImVec2(sx, sy), ImVec2(sx+320, sy+290), C_BORDER, 8.0f, 0, 1.5f);
    
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(sx+20, sy+11), C_GOLD, "\xD0\x92\xD0\x98\xD0\x97\xD0\xA3\xD0\x90\xD0\x9B\xD0\xAC\xD0\x9D\xD0\xAB\xD0\x95 \xD0\x9D\xD0\x90\xD0\xA1\xD0\xA2\xD0\xA0\xD0\x9E\xD0\x99\xD0\x9A\xD0\x98");
    dl->AddLine(ImVec2(sx+20, sy+35), ImVec2(sx+300, sy+35), C_BORDER, 1.5f);
    
    // Theme selector
    float ty1 = sy + 50;
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(sx+20, ty1+6), C_WHITE, "\xD0\xA2\xD0\xB5\xD0\xBC\xD0\xB0 \xD0\xBE\xD1\x84\xD0\xBE\xD1\x80\xD0\xBC\xD0\xBB\xD0\xB5\xD0\xBD\xD0\xB8\xD1\x8F \xD0\xBC\xD0\xB5\xD0\xBD\xD1\x8E");
    dl->AddText(fontSegoeBold12, 12.0f, ImVec2(sx+20, ty1+24), IM_COL32(107,115,127,255), "\xD0\xA6\xD0\xB2\xD0\xB5\xD1\x82\xD0\xBE\xD0\xB2\xD0\xB0\xD1\x8F \xD0\xBF\xD0\xB0\xD0\xBB\xD0\xB8\xD1\x82\xD1\x80\xD0\xB0 \xD0\xBE\xD0\xB2\xD0\xB5\xD1\x80\xD0\xBB\xD0\xB5\xD1\x8F");
    
    float bx1 = sx + 20, by1 = ty1 + 40;
    const char* tlbls[] = {"DEFAULT", "BLACK", "GREY"};
    for (int t = 0; t < 3; t++) {
        float bxx = bx1 + t*95;
        bool tAct = (currentTheme == t);
        
        ImGui::SetCursorScreenPos(ImVec2(bxx, by1));
        char tid[16]; sprintf_s(tid, "##thm%d", t);
        bool tClicked = ImGui::InvisibleButton(tid, ImVec2(90, 28));
        bool tHover = ImGui::IsItemHovered();
        
        if (tAct) {
            dl->AddRectFilled(ImVec2(bxx, by1), ImVec2(bxx+90, by1+28), C_GOLD_BG, 4.0f);
            dl->AddRect(ImVec2(bxx, by1), ImVec2(bxx+90, by1+28), C_GOLD, 4.0f, 0, 1.5f);
        } else {
            dl->AddRectFilled(ImVec2(bxx, by1), ImVec2(bxx+90, by1+28), tHover ? IM_COL32(255,255,255,10) : C_BOX, 4.0f);
            dl->AddRect(ImVec2(bxx, by1), ImVec2(bxx+90, by1+28), tHover ? C_GRAY : C_BORDER, 4.0f, 0, 1.0f);
        }
        ImVec2 ts = fontSegoeBold12->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, tlbls[t]);
        dl->AddText(fontSegoeBold12, 12.0f, ImVec2(bxx+(90-ts.x)/2, by1+(28-ts.y)/2), tAct ? C_GOLD : (tHover ? C_WHITE : C_GRAY), tlbls[t]);
        
        if (tClicked) {
            currentTheme = t;
            ApplyTheme();
            SaveSettings();
        }
    }
    
    dl->AddLine(ImVec2(sx+20, sy+135), ImVec2(sx+300, sy+135), C_BORDER, 1.0f);
    
    // Opacity
    float ty2 = sy + 150;
    dl->AddText(fontSegoeBold14, 13.0f, ImVec2(sx+20, ty2+6), C_WHITE, "\xD0\x9F\xD1\x80\xD0\xBE\xD0\xB7\xD1\x80\xD0\xB0\xD1\x87\xD0\xBD\xD0\xBE\xD1\x81\xD1\x82\xD1\x8C \xD1\x84\xD0\xBE\xD0\xBD\xD0\xB0");
    dl->AddText(fontSegoeBold12, 11.0f, ImVec2(sx+20, ty2+22), IM_COL32(107,115,127,255), "\xD0\x97\xD0\xB0\xD1\x82\xD0\xB5\xD0\xBC\xD0\xBD\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\xB8\xD0\xB3\xD1\x80\xD1\x8B \xD0\xB7\xD0\xB0 \xD0\xBE\xD0\xB2\xD0\xB5\xD1\x80\xD0\xBB\xD0\xB5\xD0\xB5\xD0\xBC");
    
    char opBuf[16]; sprintf_s(opBuf, "%d%%", (int)(settingsAlpha * 100));
    ImVec2 ops = fontSegoeBold20->CalcTextSizeA(16.0f, FLT_MAX, 0.0f, opBuf);
    dl->AddText(fontSegoeBold20, 16.0f, ImVec2(sx+300-ops.x, ty2+11), C_GOLD, opBuf);
    
    float slY = ty2+40;
    dl->AddRectFilled(ImVec2(sx+20, slY), ImVec2(sx+300, slY+6), C_BORDER, 3.0f);
    float fillW = settingsAlpha * 280.0f;
    dl->AddRectFilled(ImVec2(sx+20, slY), ImVec2(sx+20+fillW, slY+6), C_GOLD, 3.0f);
    dl->AddCircleFilled(ImVec2(sx+20+fillW, slY+3), 7.0f, C_BOX, 16);
    dl->AddCircle(ImVec2(sx+20+fillW, slY+3), 7.0f, C_GOLD, 16, 2.0f);
    
    ImGui::SetCursorScreenPos(ImVec2(sx+20, slY-6));
    ImGui::InvisibleButton("##alphaSlider", ImVec2(280, 18));
    if (ImGui::IsItemActive()) {
        float mx = ImGui::GetMousePos().x;
        settingsAlpha = (mx - (sx+20)) / 280.0f;
        if (settingsAlpha < 0.1f) settingsAlpha = 0.1f;
        if (settingsAlpha > 1.0f) settingsAlpha = 1.0f;
    }
    if (ImGui::IsItemDeactivated()) SaveSettings();

    dl->AddLine(ImVec2(sx+20, sy+240), ImVec2(sx+300, sy+240), C_BORDER, 1.0f);
    
    // VK Group
    float vX = sx + 20, vY = sy + 248;
    ImGui::SetCursorScreenPos(ImVec2(vX, vY));
    ImGui::InvisibleButton("##vkbtn", ImVec2(280, 32));
    bool vHover = ImGui::IsItemHovered();
    dl->AddRectFilled(ImVec2(vX, vY), ImVec2(vX+280, vY+32), vHover ? IM_COL32(40, 44, 52, 255) : C_BOX, 4.0f);
    dl->AddRect(ImVec2(vX, vY), ImVec2(vX+280, vY+32), C_GOLD, 4.0f, 0, 1.0f);
    dl->AddText(fontSegoeBold14, 13.0f, ImVec2(vX+15, vY+9), C_GOLD, "\xD0\x9E\xD0\xA4\xD0\x98\xD0\xA6\xD0\x98\xD0\x90\xD0\x9B\xD0\xAC\xD0\x9D\xD0\x90\xD0\xAF \xD0\x93\xD0\xA0\xD0\xA3\xD0\x9F\xD0\x9F\xD0\x90 VK");
    const char* linkTxt = "vk.com/duranhelper";
    ImVec2 linkSz = fontSegoeBold12->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, linkTxt);
    dl->AddText(fontSegoeBold12, 12.0f, ImVec2(vX+280 - 15 - linkSz.x, vY+10), vHover ? IM_COL32(147, 197, 253, 255) : IM_COL32(59, 130, 246, 255), linkTxt);
    
    // Right panel: SYSTEM AND BEHAVIOR
    float prx = o.x + 360;
    dl->AddRectFilled(ImVec2(prx, sy), ImVec2(prx+320, sy+290), C_HEADER, 8.0f);
    dl->AddRect(ImVec2(prx, sy), ImVec2(prx+320, sy+290), C_BORDER, 8.0f, 0, 1.5f);
    
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(prx+20, sy+11), C_GOLD, "\xD0\xA1\xD0\x98\xD0\xA1\xD0\xA2\xD0\x95\xD0\x9C\xD0\x90 \xD0\x98 \xD0\x9F\xD0\x9E\xD0\x92\xD0\x95\xD0\x94\xD0\x95\xD0\x9D\xD0\x98\xD0\x95");
    dl->AddLine(ImVec2(prx+20, sy+35), ImVec2(prx+300, sy+35), C_BORDER, 1.5f);
    
    // Hotkey
    float ry1 = sy + 50;
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(prx+20, ry1+6), C_WHITE, "\xD0\x9A\xD0\xBB\xD0\xB0\xD0\xB2\xD0\xB8\xD1\x88\xD0\xB0 \xD0\xB2\xD1\x8B\xD0\xB7\xD0\xBE\xD0\xB2\xD0\xB0 \xD0\xBC\xD0\xB5\xD0\xBD\xD1\x8E");
    dl->AddText(fontSegoeBold12, 12.0f, ImVec2(prx+20, ry1+24), IM_COL32(107,115,127,255), "\xD0\x9E\xD1\x82\xD0\xBA\xD1\x80\xD1\x8B\xD1\x82\xD1\x8C / \xD0\xB7\xD0\xB0\xD0\xBA\xD1\x80\xD1\x8B\xD1\x82\xD1\x8C \xD0\xBE\xD0\xB2\xD0\xB5\xD1\x80\xD0\xBB\xD0\xB5\xD0\xB9");
    
    std::string keyStr = toggleKeyStr;
    if (keyStr.empty()) keyStr = "F9";
    ImVec2 ks = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, keyStr.c_str());
    float boxW = ks.x + 20.0f;
    if (boxW < 60.0f) boxW = 60.0f;
    dl->AddRectFilled(ImVec2(prx+300-boxW, ry1+5), ImVec2(prx+300, ry1+31), C_BOX, 4.0f);
    dl->AddRect(ImVec2(prx+300-boxW, ry1+5), ImVec2(prx+300, ry1+31), C_GOLD, 4.0f, 0, 1.5f);
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(prx+300-boxW/2-ks.x/2, ry1+18-ks.y/2), C_GOLD, keyStr.c_str());
    
    dl->AddLine(ImVec2(prx+20, sy+95), ImVec2(prx+300, sy+95), C_BORDER, 1.0f);
    
    // Delay
    float ry2 = sy + 110;
    dl->AddText(fontSegoeBold14, 13.0f, ImVec2(prx+20, ry2+6), C_WHITE, "\xD0\x97\xD0\xB0\xD0\xB4\xD0\xB5\xD1\x80\xD0\xB6\xD0\xBA\xD0\xB0 \xD0\xB1\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB5\xD1\x80\xD0\xB0");
    dl->AddText(fontSegoeBold12, 11.0f, ImVec2(prx+20, ry2+22), IM_COL32(107,115,127,255), "\xD0\x9F\xD0\xB0\xD1\x83\xD0\xB7\xD0\xB0 \xD0\xBC\xD0\xB5\xD0\xB6\xD0\xB4\xD1\x83 \xD0\xBE\xD1\x82\xD0\xBF\xD1\x80\xD0\xB0\xD0\xB2\xD0\xBA\xD0\xBE\xD0\xB9 \xD1\x81\xD0\xBE\xD0\xBE\xD0\xB1\xD1\x89\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB9");
    
    char delBuf[32]; sprintf_s(delBuf, "%d \xD0\xBC\xD1\x81", binderDelay);
    ImVec2 ds = fontSegoeBold20->CalcTextSizeA(16.0f, FLT_MAX, 0.0f, delBuf);
    dl->AddText(fontSegoeBold20, 16.0f, ImVec2(prx+300-ds.x, ry2+11), C_GOLD, delBuf);
    
    float sdlY = ry2+40;
    dl->AddRectFilled(ImVec2(prx+20, sdlY), ImVec2(prx+300, sdlY+6), C_BORDER, 3.0f);
    float dfillW = (binderDelay / 2000.0f) * 280.0f;
    dl->AddRectFilled(ImVec2(prx+20, sdlY), ImVec2(prx+20+dfillW, sdlY+6), C_GOLD, 3.0f);
    dl->AddCircleFilled(ImVec2(prx+20+dfillW, sdlY+3), 7.0f, C_BOX, 16);
    dl->AddCircle(ImVec2(prx+20+dfillW, sdlY+3), 7.0f, C_GOLD, 16, 2.0f);
    
    ImGui::SetCursorScreenPos(ImVec2(prx+20, sdlY-6));
    ImGui::InvisibleButton("##delaySlider", ImVec2(280, 18));
    if (ImGui::IsItemActive()) {
        float mx = ImGui::GetMousePos().x;
        float pct = (mx - (prx+20)) / 280.0f;
        if (pct < 0.0f) pct = 0.0f;
        if (pct > 1.0f) pct = 1.0f;
        binderDelay = (int)round(pct * 20.0f) * 100;
    }
    if (ImGui::IsItemDeactivated()) SaveSettings();
    
    dl->AddLine(ImVec2(prx+20, sy+175), ImVec2(prx+300, sy+175), C_BORDER, 1.0f);
    
    auto drawToggle = [&](float ty, float tx, const char* title, const char* desc, bool& val, const char* id) {
        dl->AddText(fontSegoeBold14, 14.0f, ImVec2(tx+20, ty+6), C_WHITE, title);
        dl->AddText(fontSegoeBold12, 12.0f, ImVec2(tx+20, ty+24), IM_COL32(107,115,127,255), desc);
        
        ImU32 rc = val ? IM_COL32(46,160,67,255) : C_BORDER;
        dl->AddRectFilled(ImVec2(tx+264, ty+5), ImVec2(tx+300, ty+25), rc, 10.0f);
        if (!val) dl->AddRect(ImVec2(tx+264, ty+5), ImVec2(tx+300, ty+25), C_LINE, 10.0f, 0, 1.0f);
        dl->AddCircleFilled(ImVec2(tx + (val ? 290 : 274), ty+15), 7.0f, val ? C_WHITE : C_GRAY, 16);
        
        ImGui::SetCursorScreenPos(ImVec2(tx+264, ty+5));
        if (ImGui::InvisibleButton(id, ImVec2(36, 20))) { val = !val; SaveSettings(); }
    };

    drawToggle(sy+190, prx, "\xD0\x97\xD0\xB0\xD0\xBF\xD0\xBE\xD0\xBC\xD0\xB8\xD0\xBD\xD0\xB0\xD1\x82\xD1\x8C \xD0\xB2\xD0\xBA\xD0\xBB\xD0\xB0\xD0\xB4\xD0\xBA\xD1\x83", "\xD0\x9E\xD1\x82\xD0\xBA\xD1\x80\xD1\x8B\xD0\xB2\xD0\xB0\xD1\x82\xD1\x8C \xD0\xBC\xD0\xB5\xD0\xBD\xD1\x8E \xD0\xBD\xD0\xB0 \xD0\xBF\xD0\xBE\xD1\x81\xD0\xBB\xD0\xB5\xD0\xB4\xD0\xBD\xD0\xB5\xD0\xBC \xD1\x8D\xD0\xBA\xD1\x80\xD0\xB0\xD0\xBD\xD0\xB5", rememberTab, "##remTabBtn");
    dl->AddLine(ImVec2(prx+20, sy+230), ImVec2(prx+300, sy+230), C_BORDER, 1.0f);
    drawToggle(sy+245, prx, "\xD0\x9F\xD0\xBE\xD0\xB8\xD1\x81\xD0\xBA \xD0\xB2 \xD1\x82\xD0\xB5\xD0\xBA\xD1\x83\xD1\x89\xD0\xB5\xD0\xBC \xD1\x80\xD0\xB0\xD0\xB7\xD0\xB4\xD0\xB5\xD0\xBB\xD0\xB5", "\xD0\x98\xD1\x81\xD0\xBA\xD0\xB0\xD1\x82\xD1\x8C \xD1\x82\xD0\xBE\xD0\xBB\xD1\x8C\xD0\xBA\xD0\xBE \xD0\xB2 \xD0\xB2\xD1\x8B\xD0\xB1\xD1\x80\xD0\xB0\xD0\xBD\xD0\xBD\xD0\xBE\xD0\xBC \xD1\x80\xD0\xB0\xD0\xB7\xD0\xB4\xD0\xB5\xD0\xBB\xD0\xB5", searchCurrentSection, "##searchSectionBtn");
    
    // Block 3: SMART QUOTING
    float qy = sy + 300;
    dl->AddRectFilled(ImVec2(sx, qy), ImVec2(sx+660, qy+250), C_HEADER, 8.0f);
    dl->AddRect(ImVec2(sx, qy), ImVec2(sx+660, qy+250), C_BORDER, 8.0f, 0, 1.5f);
    
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(sx+20, qy+11), C_GOLD, "\xD0\xA3\xD0\x9C\xD0\x9D\xD0\x9E\xD0\x95 \xD0\xA6\xD0\x98\xD0\xA2\xD0\x98\xD0\xA0\xD0\x9E\xD0\x92\xD0\x90\xD0\x9D\xD0\x98\xD0\x95");
    dl->AddLine(ImVec2(sx+20, qy+35), ImVec2(sx+640, qy+35), C_BORDER, 1.5f);

    auto drawWideToggle = [&](float ty, float tx, const char* title, const char* desc, bool& val, const char* id) {
        dl->AddText(fontSegoeBold14, 14.0f, ImVec2(tx+20, ty+6), C_WHITE, title);
        dl->AddText(fontSegoeBold12, 12.0f, ImVec2(tx+20, ty+24), IM_COL32(107,115,127,255), desc);
        
        ImU32 rc = val ? IM_COL32(46,160,67,255) : C_BORDER;
        dl->AddRectFilled(ImVec2(tx+604, ty+5), ImVec2(tx+640, ty+25), rc, 10.0f);
        if (!val) dl->AddRect(ImVec2(tx+604, ty+5), ImVec2(tx+640, ty+25), C_LINE, 10.0f, 0, 1.0f);
        dl->AddCircleFilled(ImVec2(tx + (val ? 630 : 614), ty+15), 7.0f, val ? C_WHITE : C_GRAY, 16);
        
        ImGui::SetCursorScreenPos(ImVec2(tx+604, ty+5));
        if (ImGui::InvisibleButton(id, ImVec2(36, 20))) { val = !val; SaveSettings(); }
    };

    drawWideToggle(qy+50, sx, "\xD0\x92\xD0\xBA\xD0\xBB\xD1\x8E\xD1\x87\xD0\xB8\xD1\x82\xD1\x8C \xD1\x86\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5", "\xD0\xA0\xD0\xB0\xD0\xB7\xD1\x80\xD0\xB5\xD1\x88\xD0\xB8\xD1\x82\xD1\x8C \xD1\x86\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\xB7\xD0\xB0\xD0\xBA\xD0\xBE\xD0\xBD\xD0\xBE\xD0\xB2 \xD0\xB2 \xD1\x87\xD0\xB0\xD1\x82 \xD0\xBF\xD1\x80\xD0\xB8 \xD0\xBA\xD0\xBB\xD0\xB8\xD0\xBA\xD0\xB5 \xD0\xBC\xD1\x8B\xD1\x88\xD1\x8C\xD1\x8E", quoteEnabled, "##tglQ1");
    dl->AddLine(ImVec2(sx+20, qy+92), ImVec2(sx+640, qy+92), C_BORDER, 1.0f);
    drawWideToggle(qy+102, sx, "\xD0\xA0\xD0\xB0\xD1\x81\xD1\x88\xD0\xB8\xD1\x80\xD0\xB5\xD0\xBD\xD0\xBD\xD0\xBE\xD0\xB5 \xD1\x86\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5", "\xD0\x90\xD0\xB2\xD1\x82\xD0\xBE\xD0\xBC\xD0\xB0\xD1\x82\xD0\xB8\xD1\x87\xD0\xB5\xD1\x81\xD0\xBA\xD0\xB8 \xD0\xB4\xD0\xBE\xD0\xB1\xD0\xB0\xD0\xB2\xD0\xBB\xD1\x8F\xD1\x82\xD1\x8C \xD0\xBD\xD0\xB0\xD0\xB7\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD1\x80\xD0\xB0\xD0\xB7\xD0\xB4\xD0\xB5\xD0\xBB\xD0\xB0 \xD0\xB8 \xD0\xB3\xD0\xBB\xD0\xB0\xD0\xB2\xD1\x8B \xD0\xBA \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD0\xB5", quoteExtended, "##tglQ2");
    dl->AddLine(ImVec2(sx+20, qy+144), ImVec2(sx+640, qy+144), C_BORDER, 1.0f);
    drawWideToggle(qy+154, sx, "\xD0\xA7\xD1\x82\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB5 \xD1\x86\xD0\xB5\xD0\xBB\xD0\xBE\xD0\xB9 \xD0\xB3\xD0\xBB\xD0\xB0\xD0\xB2\xD1\x8B", "\xD0\x97\xD0\xB0\xD1\x87\xD0\xB8\xD1\x82\xD1\x8B\xD0\xB2\xD0\xB0\xD1\x82\xD1\x8C \xD0\xB2\xD1\x81\xD0\xB5 \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD1\x8C\xD0\xB8 \xD0\xB2 \xD0\xB3\xD0\xBB\xD0\xB0\xD0\xB2\xD0\xB5 \xD0\xBF\xD1\x80\xD0\xB8 ALT-\xD0\xBA\xD0\xBB\xD0\xB8\xD0\xBA\xD0\xB5 \xD0\xBD\xD0\xB0 \xD0\xB5\xD1\x91 \xD0\xB7\xD0\xB0\xD0\xB3\xD0\xBE\xD0\xBB\xD0\xBE\xD0\xB2\xD0\xBE\xD0\xBA", quoteChapter, "##tglQ3");
    dl->AddLine(ImVec2(sx+20, qy+196), ImVec2(sx+640, qy+196), C_BORDER, 1.0f);
    drawWideToggle(qy+206, sx, "\xD0\xA6\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84\xD0\xBE\xD0\xB2", "\xD0\x90\xD0\xB2\xD1\x82\xD0\xBE\xD0\xBC\xD0\xB0\xD1\x82\xD0\xB8\xD1\x87\xD0\xB5\xD1\x81\xD0\xBA\xD0\xB8 \xD0\xB2\xD1\x8B\xD0\xB2\xD0\xBE\xD0\xB4\xD0\xB8\xD1\x82\xD1\x8C \xD1\x82\xD0\xB5\xD0\xBA\xD1\x81\xD1\x82 \xD1\x81\xD1\x82\xD0\xB0\xD1\x82\xD0\xB5\xD0\xB9 \xD0\xB2 \xD1\x87\xD0\xB0\xD1\x82 \xD0\xBF\xD0\xBE\xD1\x81\xD0\xBB\xD0\xB5 \xD0\xB2\xD1\x8B\xD0\xBF\xD0\xB8\xD1\x81\xD0\xBA\xD0\xB8 \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84\xD0\xB0", quoteFines, "##tglQ4");

    // Bottom separator (spanning full width)
    float bY = qy + 255;
    dl->AddLine(ImVec2(sx, bY), ImVec2(sx+660, bY), C_BORDER, 1.5f);
    
    // Version & Reset
    dl->AddText(fontSegoeBold12, 12.0f, ImVec2(sx, bY+27), IM_COL32(92,99,112,255), "\xD0\x92\xD0\xB5\xD1\x80\xD1\x81\xD0\xB8\xD1\x8F \xD0\xBF\xD0\xBB\xD0\xB0\xD0\xB3\xD0\xB8\xD0\xBD\xD0\xB0:");
    char vBuf[32]; sprintf_s(vBuf, "v%s ASI", versionStr.c_str());
    ImVec2 vs = fontSegoeBold12->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, "\xD0\x92\xD0\xB5\xD1\x80\xD1\x81\xD0\xB8\xD1\x8F \xD0\xBF\xD0\xBB\xD0\xB0\xD0\xB3\xD0\xB8\xD0\xBD\xD0\xB0: ");
    dl->AddText(fontSegoeBold12, 12.0f, ImVec2(sx+vs.x, bY+27), C_GRAY, vBuf);
    
    float resW = 190, resH = 34;
    float resX = sx + 660 - resW;
    
    ImGui::SetCursorScreenPos(ImVec2(resX, bY+10));
    bool resClicked = ImGui::InvisibleButton("##resetBtn", ImVec2(resW, resH));
    bool resHover = ImGui::IsItemHovered();
    
    dl->AddRectFilled(ImVec2(resX, bY+10), ImVec2(resX+resW, bY+10+resH), resHover ? IM_COL32(239, 68, 68, 40) : C_BOX, 6.0f);
    dl->AddRect(ImVec2(resX, bY+10), ImVec2(resX+resW, bY+10+resH), resHover ? IM_COL32(248, 113, 113, 255) : C_RED, 6.0f, 0, 1.5f);
    
    float rix = resX + 18, riy = bY + 10 + 17;
    dl->AddCircle(ImVec2(rix, riy), 6.0f, resHover ? IM_COL32(248, 113, 113, 255) : C_RED, 24, 1.5f);
    dl->AddRectFilled(ImVec2(rix-6, riy-8), ImVec2(rix-2, riy), resHover ? IM_COL32(45, 20, 20, 255) : C_BOX);
    dl->AddLine(ImVec2(rix-4, riy-6), ImVec2(rix-4, riy-9), resHover ? IM_COL32(248, 113, 113, 255) : C_RED, 1.5f);
    dl->AddLine(ImVec2(rix-4, riy-6), ImVec2(rix-1, riy-6), resHover ? IM_COL32(248, 113, 113, 255) : C_RED, 1.5f);
    
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(resX+36, bY+10+8), resHover ? IM_COL32(254, 226, 226, 255) : C_RED, "\xD0\xA1\xD0\x91\xD0\xA0\xD0\x9E\xD0\xA1\xD0\x98\xD0\xA2\xD0\xAC \xD0\x9D\xD0\x90\xD0\xA1\xD0\xA2\xD0\xA0\xD0\x9E\xD0\x99\xD0\x9A\xD0\x98");
    
    if (resClicked) {
        toggleKey = VK_F9;
        currentTheme = 0;
        settingsAlpha = 0.85f;
        binderDelay = 1000;
        rememberTab = true;
        searchCurrentSection = true;
        quoteEnabled = true;
        quoteExtended = false;
        quoteChapter = false;
        quoteFines = true;
        ApplyTheme();
        SaveSettings();
    }
    
    // Add bottom padding so reset button isn't clipped by scroll window
    ImGui::SetCursorScreenPos(ImVec2(sx, bY + 45));
    ImGui::Dummy(ImVec2(0, 5));
    
    ImGui::EndChild();
    ImGui::PopStyleColor(4);
    ImGui::PopStyleVar();
}
// ===== Database Stub =====
void Gui::RenderDatabaseStub(ImDrawList* dl, ImVec2 o) {
    float cx = o.x + W/2, cy = o.y + H/2;
    const char* txt = "\xD0\x92 \xD0\xa0\xD0\x90\xD0\x97\xD0\xa0\xD0\x90\xD0\x91\xD0\x9E\xD0\xa2\xD0\x9A\xD0\x95..."; // "Р’ Р РђР—Р РђР‘РћРўРљР•..."
    ImVec2 ts = fontSegoeBold20->CalcTextSizeA(13.0f, FLT_MAX, 0.0f, txt);
    dl->AddText(fontSegoeBold20, 13.0f, ImVec2(cx-ts.x/2, cy-ts.y/2), C_GRAY, txt);
}

// ===== Main Render =====
void Gui::Render() {
    // FIRMLY PREVENT ImGui from reading WantSetMousePos from the last frame and teleporting the OS cursor!
    ImGui::GetIO().WantSetMousePos = false;

    ImGui_ImplDX9_NewFrame();
    ImGui_ImplWin32_NewFrame();

    // Re-enable scaling relative to baseline 1366x768
    ImVec2 real_ds = ImGui::GetIO().DisplaySize;
    float rx = real_ds.x / 1366.0f;
    float ry = real_ds.y / 768.0f;
    float rs = (rx < ry) ? rx : ry;
    if (rs < 0.5f) rs = 0.5f;
    globalScale = rs;
    g_ImGuiGlobalScale = rs;

    // Virtualize screen bounds natively at baseline scale
    ImGui::GetIO().DisplaySize.x = real_ds.x / rs;
    ImGui::GetIO().DisplaySize.y = real_ds.y / rs;

    ImGui::NewFrame();
    
    // Clear search buffers safely inside the ImGui frame when Toggle requested it
    if (clearNextFrame) {
        memset(searchLaws, 0, sizeof(searchLaws));
        memset(searchFines, 0, sizeof(searchFines));
        memset(searchBinder, 0, sizeof(searchBinder));
        memset(fineIdBuf, 0, sizeof(fineIdBuf));
        clearNextFrame = false;
    }

    if (show || radialMenuOpen || radialIdInputOpen) {
        ImGui::GetIO().MouseDrawCursor = true;
        ImGui::GetIO().ConfigFlags &= ~ImGuiConfigFlags_NoMouseCursorChange;
    } else {
        ImGui::GetIO().MouseDrawCursor = false;
        ImGui::GetIO().ConfigFlags |= ImGuiConfigFlags_NoMouseCursorChange;
    }

    float targetAlpha = show ? 1.0f : 0.0f;
    if (!show) {
        alpha = 0.0f; // Instant close РІР‚вЂќ no fade out
    } else {
        alpha += (targetAlpha - alpha) * 0.35f; // Smooth fade in
    }

    if (alpha >= 0.01f) {
        ImGui::PushStyleVar(ImGuiStyleVar_Alpha, alpha);

        // Center the overlay on screen
        ImVec2 ds = ImGui::GetIO().DisplaySize;
        ImVec2 origin((ds.x - W) / 2.0f, (ds.y - H) / 2.0f);
        // No vertical slide РІР‚вЂќ pure alpha fade only

        ImGui::SetNextWindowPos(ImVec2(0, 0));
        ImGui::SetNextWindowSize(ds);
        ImGui::Begin("##DuranHUD", nullptr,
            ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoBackground |
            ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoBringToFrontOnFocus);

        ImDrawList* dl = ImGui::GetWindowDrawList();

        // Draw HUD frame
        DrawHudFrame(dl, origin);
        DrawTabs(dl, origin);

        // Close button click detection - 700x432: x=655 y=12
        ImGui::SetCursorScreenPos(ImVec2(origin.x+655, origin.y+12));
        if (ImGui::InvisibleButton("##closeBtn", ImVec2(26, 26)))
            Toggle();

        // Tab click detection - 700x432: x=260, widths 80, 160, 80, gap=10
        float tabX = origin.x + 260;
        float tabWidths[] = {80, 160, 80};
        for (int i = 0; i < 3; i++) {
            ImGui::SetCursorScreenPos(ImVec2(tabX, origin.y+12));
            char tid[16]; sprintf_s(tid, "##tab%d", i);
            if (ImGui::InvisibleButton(tid, ImVec2(tabWidths[i], 26))) {
                activeTab = i;
                showLawDropdown = false;
                showSettings = false; // Close settings when switching tabs
            }
            tabX += tabWidths[i] + 10;
        }

        // Gear button click РІР‚вЂќ right of last tab
        float gearX = tabX + 5;
        ImGui::SetCursorScreenPos(ImVec2(gearX, origin.y+12));
        if (ImGui::InvisibleButton("##gearBtn", ImVec2(26, 26))) {
            showSettings = !showSettings;
        }

        // Render active tab content OR settings panel
        if (showSettings) {
            RenderSettingsTab(dl, origin);
        } else {
            switch (activeTab) {
                case 0: RenderLawsTab(dl, origin); break;
                case 1: RenderFinesTab(dl, origin); break;
                case 2: RenderBinderTab(dl, origin); break;
                case 3: RenderDatabaseStub(dl, origin); break;
            }
        }

        // Dropdown click handling (laws tab) - raw mouse checks
        // because InvisibleButtons overlap with scroll child region
        if (activeTab == 0 && ImGui::IsMouseClicked(0)) {
            ImVec2 mp = ImGui::GetMousePos();
            float dbx = origin.x + 470, dby = origin.y + 65;
            
            if (showLawDropdown && !lawSections.empty()) {
                float ddY = dby + 36;
                float ddH = (float)lawSections.size() * 36.0f + 10.0f;
                bool clickedItem = false;
                // Check item clicks
                for (int i = 0; i < (int)lawSections.size(); i++) {
                    float iy = ddY + 8 + i * 36;
                    if (mp.x >= dbx+6 && mp.x <= dbx+204 && mp.y >= iy && mp.y <= iy+32) {
                        if (selectedLawSection != i) {
                            selectedLawSection = i;
                            resetLawsScroll = true;
                        }
                        showLawDropdown = false;
                        clickedItem = true;
                        break;
                    }
                }
                if (!clickedItem) {
                    bool inBtn = (mp.x >= dbx && mp.x <= dbx+210 && mp.y >= dby && mp.y <= dby+32);
                    bool inList = (mp.x >= dbx && mp.x <= dbx+210 && mp.y >= ddY && mp.y <= ddY+ddH);
                    if (!inBtn && !inList) {
                        showLawDropdown = false;
                    }
                }
            }
        }

        if (overlayErrorTimer > 0.0f) {
            overlayErrorTimer -= ImGui::GetIO().DeltaTime;
            float eh = 44.0f;
            float ew = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, overlayErrorMsg.c_str()).x + 30.0f;
            float ex = origin.x + (W - ew) / 2.0f; 
            float ey = origin.y + 10.0f;
            
            dl->AddRectFilled(ImVec2(ex, ey), ImVec2(ex+ew, ey+eh), C_BOX, 8.0f);
            dl->AddRect(ImVec2(ex, ey), ImVec2(ex+ew, ey+eh), overlayErrorColor, 8.0f, 0, 1.5f);
            dl->AddText(fontSegoeBold14, 14.0f, ImVec2(ex+15, ey+15), overlayErrorColor, overlayErrorMsg.c_str());
        }



        ImGui::End();
        ImGui::PopStyleVar();
    }
    // ===== Radial Menu (independent of overlay) =====
    if (radialMenuOpen || radialIdInputOpen) {
        ImVec2 ds2 = ImGui::GetIO().DisplaySize;
        ImGui::SetNextWindowPos(ImVec2(0, 0));
        ImGui::SetNextWindowSize(ds2);
        ImGui::PushStyleVar(ImGuiStyleVar_Alpha, 1.0f);
        ImGuiWindowFlags radialFlags = ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoBackground |
            ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoBringToFrontOnFocus;
        if (!radialIdInputOpen) radialFlags |= ImGuiWindowFlags_NoInputs;
        ImGui::Begin("##RadialOverlay", nullptr, radialFlags);
        if (radialMenuOpen) RenderRadialMenu();
        if (radialIdInputOpen) RenderRadialIdInput();
        ImGui::End();
        ImGui::PopStyleVar();
    }

    ImGui::EndFrame();
    ImGui::Render();
    
    ImDrawData* draw_data = ImGui::GetDrawData();

    // Scale back up to physical pixels before DX9 renders
    if (draw_data) {
        float rx_post = real_ds.x / 1366.0f;
        float ry_post = real_ds.y / 768.0f;
        float rs_post = (rx_post < ry_post) ? rx_post : ry_post;
        if (rs_post < 0.5f) rs_post = 0.5f;

        if (rs_post != 1.0f) {
            for (int i = 0; i < draw_data->CmdListsCount; i++) {
                ImDrawList* cmd_list = draw_data->CmdLists[i];
                for (int v = 0; v < cmd_list->VtxBuffer.Size; v++) {
                    ImDrawVert* vert = &cmd_list->VtxBuffer.Data[v];
                    vert->pos.x *= rs_post;
                    vert->pos.y *= rs_post;
                }
            }
            
            draw_data->DisplaySize.x *= rs_post;
            draw_data->DisplaySize.y *= rs_post;
            draw_data->DisplayPos.x *= rs_post;
            draw_data->DisplayPos.y *= rs_post;
            for (int n = 0; n < draw_data->CmdListsCount; n++) {
                ImDrawList* cmd_list = draw_data->CmdLists[n];
                for (int cmd_i = 0; cmd_i < cmd_list->CmdBuffer.Size; cmd_i++) {
                    ImDrawCmd* pcmd = &cmd_list->CmdBuffer[cmd_i];
                    pcmd->ClipRect.x *= rs_post;
                    pcmd->ClipRect.y *= rs_post;
                    pcmd->ClipRect.z *= rs_post;
                    pcmd->ClipRect.w *= rs_post;
                }
            }
        }
    }

    ImGui_ImplDX9_RenderDrawData(draw_data);
}

// ===== Radial Menu Rendering =====
void Gui::DrawRadialIcon(ImDrawList* dl, ImVec2 c, float sz, const std::string& icon, ImU32 col) {
    std::string iconKey = icon;
    std::transform(iconKey.begin(), iconKey.end(), iconKey.begin(), [](unsigned char ch) { return (char)std::tolower(ch); });
    iconKey.erase(std::remove_if(iconKey.begin(), iconKey.end(), [](unsigned char ch) {
        return ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r';
    }), iconKey.end());
    if (iconKey == "idcard" || iconKey == "id" || iconKey == "card") iconKey = "badge";
    if (iconKey == "doc" || iconKey == "paper" || iconKey == "file") iconKey = "document";
    if (iconKey == "speaker" || iconKey == "bullhorn") iconKey = "megaphone";
    if (iconKey == "bolt") iconKey = "lightning";

    float r = sz * 0.5f;
    if (iconKey == "star") {
        ImVec2 pts[10];
        const float outerR = r * 0.85f;
        const float innerR = r * 0.48f;
        for (int i = 0; i < 5; ++i) {
            float aOuter = -3.14159f / 2.0f + i * 2.0f * 3.14159f / 5.0f;
            float aInner = aOuter + 3.14159f / 5.0f;
            pts[i * 2] = ImVec2(c.x + outerR * cosf(aOuter), c.y + outerR * sinf(aOuter));
            pts[i * 2 + 1] = ImVec2(c.x + innerR * cosf(aInner), c.y + innerR * sinf(aInner));
        }
        
        ImDrawListFlags oldFlags = dl->Flags;
        dl->Flags &= ~ImDrawListFlags_AntiAliasedFill;
        
        for (int i = 0; i < 10; ++i) {
            dl->AddTriangleFilled(c, pts[i], pts[(i + 1) % 10], col);
        }
        
        dl->Flags = oldFlags;
        dl->AddPolyline(pts, 10, col, ImDrawFlags_Closed, 1.0f);
    } else if (iconKey == "megaphone") {
        dl->AddCircle(ImVec2(c.x - r*0.34f, c.y), r*0.38f, col, 18, 1.9f);
        dl->AddCircle(ImVec2(c.x + r*0.34f, c.y), r*0.38f, col, 18, 1.9f);
        dl->AddLine(ImVec2(c.x - r*0.34f, c.y - r*0.38f), ImVec2(c.x + r*0.34f, c.y - r*0.38f), col, 1.9f);
    } else if (iconKey == "lightning") {
        dl->AddLine(ImVec2(c.x, c.y-r*0.95f), ImVec2(c.x-r*0.34f, c.y-r*0.02f), col, 2.2f);
        dl->AddLine(ImVec2(c.x-r*0.34f, c.y-r*0.02f), ImVec2(c.x+r*0.22f, c.y-r*0.02f), col, 2.2f);
        dl->AddLine(ImVec2(c.x+r*0.22f, c.y-r*0.02f), ImVec2(c.x-r*0.08f, c.y+r*0.94f), col, 2.2f);
    } else if (iconKey == "document") {
        dl->AddRect(ImVec2(c.x-r*0.5f, c.y-r*0.68f), ImVec2(c.x+r*0.5f, c.y+r*0.68f), col, 2.0f, 0, 1.8f);
        dl->AddLine(ImVec2(c.x-r*0.28f, c.y-r*0.3f), ImVec2(c.x+r*0.28f, c.y-r*0.3f), col, 1.3f);
        dl->AddLine(ImVec2(c.x-r*0.28f, c.y), ImVec2(c.x+r*0.28f, c.y), col, 1.3f);
        dl->AddLine(ImVec2(c.x-r*0.28f, c.y+r*0.3f), ImVec2(c.x+r*0.08f, c.y+r*0.3f), col, 1.3f);
    } else if (iconKey == "car") {
        dl->AddRect(ImVec2(c.x-r*0.68f, c.y-r*0.26f), ImVec2(c.x+r*0.68f, c.y+r*0.2f), col, 3.0f, 0, 1.8f);
        dl->AddCircleFilled(ImVec2(c.x-r*0.38f, c.y+r*0.34f), r*0.18f, col);
        dl->AddCircleFilled(ImVec2(c.x+r*0.38f, c.y+r*0.34f), r*0.18f, col);
    } else if (iconKey == "badge") {
        dl->AddRect(ImVec2(c.x-r*0.4f, c.y-r*0.58f), ImVec2(c.x+r*0.4f, c.y+r*0.58f), col, 2.0f, 0, 1.8f);
        dl->AddLine(ImVec2(c.x-r*0.24f, c.y-r*0.3f), ImVec2(c.x+r*0.24f, c.y-r*0.3f), col, 1.3f);
        dl->AddLine(ImVec2(c.x-r*0.24f, c.y), ImVec2(c.x+r*0.24f, c.y), col, 1.3f);
        dl->AddLine(ImVec2(c.x-r*0.24f, c.y+r*0.3f), ImVec2(c.x+r*0.24f, c.y+r*0.3f), col, 1.3f);
    } else if (iconKey == "radio") {
        dl->AddRect(ImVec2(c.x-r*0.4f, c.y-r*0.3f), ImVec2(c.x+r*0.4f, c.y+r*0.6f), col, 2.0f, 0, 1.8f);
        dl->AddLine(ImVec2(c.x, c.y-r*0.3f), ImVec2(c.x+r*0.3f, c.y-r*0.8f), col, 1.8f);
        dl->AddCircleFilled(ImVec2(c.x+r*0.3f, c.y-r*0.8f), 1.8f, col);
    } else {
        // Default: filled circle
        dl->AddCircleFilled(c, r*0.4f, col);
    }
}

void Gui::RenderRadialMenu() {
    // Prevent GTA SA from turning camera around (Look Behind) if MMB is held
    *(uint8_t*)0xB7341A = 0; // NewState.mmb
    *(uint8_t*)0xB73406 = 0; // OldState.mmb
    
    ImDrawList* dl = ImGui::GetForegroundDrawList();
    ImVec2 ds = ImGui::GetIO().DisplaySize;
    float cx = ds.x * 0.5f, cy = ds.y * 0.5f;
    
    ImVec2 mp = ImGui::GetIO().MousePos;
    
    if (radialJustOpened) {
        // Force ImGui cursor to virtual screen center (matches SetCursorPos in WndProc)
        ImGui::GetIO().MousePos = ImVec2(cx, cy);
        mp = ImVec2(cx, cy);
        radialJustOpened = false;
    }
    
    float dx = mp.x - cx, dy = mp.y - cy;
    float dist = sqrtf(dx*dx + dy*dy);
    float mouseAngle = atan2f(dy, dx) * 180.0f / 3.14159f;

    radialHoveredSector = -1;
    radialHoveredGroup = -1;

    ImU32 themeBg = (C_BOX & 0x00FFFFFF) | (222 << 24);
    ImU32 glowCol = (C_GOLD & 0x00FFFFFF) | (30 << 24);

    if (radialMode == "Grouped") {
        float innerR = 42.0f;
        float groupR = 150.0f;
        float sectorR = 255.0f;
        float groupInnerR = 42.0f;
        
        int nGroups = radialGroupCount;
        if (nGroups < 1) nGroups = 1;
        double grpStep = 360.0 / nGroups;
        double grpStartOffset = 270.0 - (grpStep / 2.0); // Group 0 is UP
        
        // --- Determine Hovered ---
        if (dist > innerR && dist <= groupR) {
            // Mouse is on the group ring
            double normAngle = mouseAngle - grpStartOffset;
            while (normAngle < 0) normAngle += 360.0;
            while (normAngle >= 360.0) normAngle -= 360.0;
            int hovG = (int)(normAngle / grpStep);
            if (hovG >= nGroups) hovG = nGroups - 1;
            radialHoveredGroup = hovG;
            radialSelectedGroup = hovG; // auto-select on hover
        } else if (dist > groupR && radialSelectedGroup != -1) {
            // Mouse is in the outer sector area
            radialHoveredGroup = radialSelectedGroup;
            int nBinds = (radialSelectedGroup < (int)radialGroups.size()) ? radialGroups[radialSelectedGroup].sectorCount : 0;
            if (nBinds > 0) {
                double activeGroupMid = grpStartOffset + radialSelectedGroup * grpStep + grpStep / 2.0;
                double fanTotal = nBinds * 60.0;
                double fanStart = activeGroupMid - fanTotal / 2.0;
                
                double diff = mouseAngle - fanStart;
                while (diff < 0) diff += 360.0;
                while (diff >= 360.0) diff -= 360.0;
                
                if (diff >= 0 && diff < fanTotal) {
                    radialHoveredSector = (int)(diff / 60.0);
                    if (radialHoveredSector >= nBinds) radialHoveredSector = nBinds - 1;
                }
            }
        } else if (dist <= innerR) {
            radialSelectedGroup = -1;
            radialHoveredGroup = -1;
        }

        auto drawDonutSector = [&](float a1, float a2, float rIn, float rOut, ImU32 col, ImU32 divCol, bool hovered, bool isThickDiv) {
            const int steps = 24;
            ImVec2 outerPts[steps + 1];
            ImVec2 innerPts[steps + 1];
            double radA1 = a1 * 3.14159 / 180.0;
            double radA2 = a2 * 3.14159 / 180.0;
            for (int s = 0; s <= steps; s++) {
                double a = radA1 + (radA2 - radA1) * s / steps;
                float ca = cosf((float)a), sa = sinf((float)a);
                outerPts[s] = ImVec2(cx + rOut * ca, cy + rOut * sa);
                innerPts[s] = ImVec2(cx + rIn * ca, cy + rIn * sa);
            }
            auto oldFlags = dl->Flags;
            dl->Flags &= ~ImDrawListFlags_AntiAliasedFill;
            for (int s = 0; s < steps; s++) {
                ImVec2 quad[4] = { outerPts[s], outerPts[s+1], innerPts[s+1], innerPts[s] };
                dl->AddConvexPolyFilled(quad, 4, col);
                if (hovered) dl->AddConvexPolyFilled(quad, 4, glowCol);
            }
            dl->Flags = oldFlags;

            ImVec2 lineStart(cx + rIn * cosf((float)radA1), cy + rIn * sinf((float)radA1));
            ImVec2 lineEnd(cx + rOut * cosf((float)radA1), cy + rOut * sinf((float)radA1));
            dl->AddLine(lineStart, lineEnd, divCol, 2.2f);

            if (hovered || isThickDiv) {
                ImVec2 lineStart2(cx + rIn * cosf((float)radA2), cy + rIn * sinf((float)radA2));
                ImVec2 lineEnd2(cx + rOut * cosf((float)radA2), cy + rOut * sinf((float)radA2));
                dl->AddLine(lineStart2, lineEnd2, divCol, 2.2f);
            }
        };

        // ═══ INNER RING (Groups) ═══
        for (int i = 0; i < nGroups; i++) {
            double a1 = grpStartOffset + i * grpStep;
            double a2 = a1 + grpStep;
            bool sel = (i == radialSelectedGroup);
            bool hov = (i == radialHoveredGroup) && (radialSelectedGroup == -1 || radialSelectedGroup != i);
            
            // If another group is selected, draw others darker
            ImU32 groupColor = themeBg;
            if (sel) groupColor = (C_GOLD & 0x00FFFFFF) | (68 << 24);
            else if (hov) groupColor = (C_GOLD & 0x00FFFFFF) | (40 << 24);
            else if (radialSelectedGroup != -1) groupColor = (C_BOX & 0x00FFFFFF) | (200 << 24);

            ImU32 divColor = sel ? ((C_GOLD & 0x00FFFFFF) | (230 << 24)) : ((C_LINE & 0x00FFFFFF) | (210 << 24));
            
            drawDonutSector((float)a1, (float)a2, innerR, groupR, groupColor, divColor, (hov || sel), sel);

            // Group Name
            double midA = a1 + grpStep / 2.0;
            float tx = cx + 96.0f * cosf((float)(midA * 3.14159 / 180.0));
            float ty = cy + 96.0f * sinf((float)(midA * 3.14159 / 180.0));
            
            std::string gName = (i < (int)radialGroups.size()) ? radialGroups[i].name : ("\xD0\x93\xD0\xA0 " + std::to_string(i+1));
            float fSize = gName.length() > 8 ? 15.0f : 17.0f;
            ImVec2 ts = fontSegoeBold14->CalcTextSizeA(fSize, FLT_MAX, 0.0f, gName.c_str());
            ImU32 tCol = sel ? C_WHITE : ((C_WHITE & 0x00FFFFFF) | (220 << 24));
            dl->AddText(fontSegoeBold14, fSize, ImVec2(tx - ts.x * 0.5f, ty - ts.y * 0.5f), tCol, gName.c_str());

            // Active Group Arrows
            if (sel) {
                float midRad = (float)(midA * 3.14159 / 180.0);
                float arrowX = cx + 65.0f * cosf(midRad);
                float arrowY = cy + 65.0f * sinf(midRad);
                
                // We rotate the arrow polygon based on midA (pointing outward)
                float ca = cosf(midRad);
                float sa = sinf(midRad);
                auto rotPt = [&](float along, float perp) {
                    return ImVec2(arrowX + along * ca - perp * sa, arrowY + along * sa + perp * ca);
                };

                ImVec2 arrow1[3] = { rotPt(-5, -8), rotPt(3, 0), rotPt(-5, 8) };
                dl->AddConvexPolyFilled(arrow1, 3, (C_GOLD & 0x00FFFFFF) | (153 << 24));
                ImVec2 arrow2[3] = { rotPt(-12, -8), rotPt(-4, 0), rotPt(-12, 8) };
                dl->AddConvexPolyFilled(arrow2, 3, (C_GOLD & 0x00FFFFFF) | (77 << 24));
            }
        }
        
        // Draw active/hovered group edges on top
        if (radialSelectedGroup >= 0 && radialSelectedGroup < nGroups) {
            double selA1 = (grpStartOffset + radialSelectedGroup * grpStep) * 3.14159 / 180.0;
            double selA2 = (grpStartOffset + (radialSelectedGroup + 1) * grpStep) * 3.14159 / 180.0;
            dl->AddLine(ImVec2(cx + innerR * cosf((float)selA1), cy + innerR * sinf((float)selA1)), 
                        ImVec2(cx + groupR * cosf((float)selA1), cy + groupR * sinf((float)selA1)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
            dl->AddLine(ImVec2(cx + innerR * cosf((float)selA2), cy + innerR * sinf((float)selA2)), 
                        ImVec2(cx + groupR * cosf((float)selA2), cy + groupR * sinf((float)selA2)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
        }

        // РІвЂўС’РІвЂўС’РІвЂўС’ OUTER RING (Sectors) РІвЂўС’РІвЂўС’РІвЂўС’
        if (radialSelectedGroup != -1 && radialSelectedGroup < (int)radialGroups.size()) {
            int nBinds = radialGroups[radialSelectedGroup].sectorCount;
            auto& activeSectors = radialGroups[radialSelectedGroup].sectors;
            
            float secInnerR = 165.0f;
            float secOuterR = 270.0f;
            float labelR = 217.0f;

            double activeGroupMid = grpStartOffset + radialSelectedGroup * grpStep + grpStep / 2.0;
            double fanTotal = nBinds * 60.0;
            double fanStart = activeGroupMid - fanTotal / 2.0;

            for (int i = 0; i < nBinds; i++) {
                double a1 = fanStart + i * 60.0;
                double a2 = a1 + 60.0;
                bool hov = (i == radialHoveredSector);
                
                ImU32 secCol = hov ? ((C_GOLD & 0x00FFFFFF) | (51 << 24)) : ((C_BOX & 0x00FFFFFF) | (234 << 24));
                ImU32 divCol = ((C_LINE & 0x00FFFFFF) | (178 << 24));

                drawDonutSector((float)a1, (float)a2, secInnerR, secOuterR, secCol, divCol, hov, false);

                double midA = a1 + 30.0;
                float tx = cx + labelR * cosf((float)(midA * 3.14159 / 180.0));
                float ty = cy + labelR * sinf((float)(midA * 3.14159 / 180.0));
                
                std::string bName = (i < (int)activeSectors.size()) ? activeSectors[i].bindName : "";
                if (bName.empty()) bName = "\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4 " + std::to_string(i + 1);

                std::vector<std::string> words;
                size_t pos = 0;
                while (pos < bName.size()) {
                    while (pos < bName.size() && bName[pos] == ' ') ++pos;
                    if (pos >= bName.size()) break;
                    size_t next = bName.find(' ', pos);
                    if (next == std::string::npos) next = bName.size();
                    words.push_back(bName.substr(pos, next - pos));
                    pos = next;
                }

                std::vector<std::string> lines;
                if (words.size() >= 2 && bName.length() > 11) {
                    size_t split = (words.size() + 1) / 2;
                    std::string l1, l2;
                    for (size_t wi = 0; wi < split; ++wi) { if (!l1.empty()) l1 += " "; l1 += words[wi]; }
                    for (size_t wi = split; wi < words.size(); ++wi) { if (!l2.empty()) l2 += " "; l2 += words[wi]; }
                    lines.push_back(l1);
                    lines.push_back(l2);
                } else {
                    lines.push_back(bName);
                }

                float fSizeB = 15.0f;
                if (bName.length() > 14 && lines.size() == 1) fSizeB = 13.0f;

                ImU32 tCol = hov ? C_WHITE : ((C_WHITE & 0x00FFFFFF) | (220 << 24));
                ImU32 iconCol = hov ? C_GOLD : ((C_GOLD & 0x00FFFFFF) | (180 << 24));
                
                float totalH = lines.size() * fSizeB * 1.05f;
                float startY = ty - 8.0f - totalH * 0.5f;

                for (size_t li = 0; li < lines.size(); ++li) {
                    ImVec2 ts = fontSegoeBold14->CalcTextSizeA(fSizeB, FLT_MAX, 0.0f, lines[li].c_str());
                    dl->AddText(fontSegoeBold14, fSizeB, ImVec2(tx - ts.x * 0.5f, startY + li * (fSizeB * 1.05f)), tCol, lines[li].c_str());
                }
                
                if (i < (int)activeSectors.size() && !activeSectors[i].bindId.empty()) {
                    float iconYOffset = lines.size() > 1 ? 22.0f : 16.0f;
                    DrawRadialIcon(dl, ImVec2(tx, ty + iconYOffset), 22.0f, activeSectors[i].icon, iconCol);
                }
            }

            // Draw hovered bind sector edges on top
            if (radialHoveredSector >= 0 && radialHoveredSector < nBinds) {
                double ha1 = (fanStart + radialHoveredSector * 60.0) * 3.14159 / 180.0;
                double ha2 = (fanStart + (radialHoveredSector + 1) * 60.0) * 3.14159 / 180.0;
                dl->AddLine(ImVec2(cx + secInnerR * cosf((float)ha1), cy + secInnerR * sinf((float)ha1)), 
                            ImVec2(cx + secOuterR * cosf((float)ha1), cy + secOuterR * sinf((float)ha1)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
                dl->AddLine(ImVec2(cx + secInnerR * cosf((float)ha2), cy + secInnerR * sinf((float)ha2)), 
                            ImVec2(cx + secOuterR * cosf((float)ha2), cy + secOuterR * sinf((float)ha2)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
            }

            // Outer and Inner decorative arcs
            ImU32 arcCol = (C_LINE & 0x00FFFFFF) | (153 << 24);
            if (nBinds >= 6) {
                dl->AddCircle(ImVec2(cx, cy), secOuterR, arcCol, 64, 4.0f);
                dl->AddCircle(ImVec2(cx, cy), secInnerR, arcCol, 64, 4.0f);
            } else {
                const int steps = 40;
                double radA1 = fanStart * 3.14159 / 180.0;
                double radA2 = (fanStart + fanTotal) * 3.14159 / 180.0;
                for (int s = 0; s < steps; s++) {
                    double a = radA1 + (radA2 - radA1) * s / steps;
                    double nextA = radA1 + (radA2 - radA1) * (s + 1) / steps;
                    
                    // Outer arc
                    dl->AddLine(ImVec2(cx + secOuterR * cosf((float)a), cy + secOuterR * sinf((float)a)),
                                ImVec2(cx + secOuterR * cosf((float)nextA), cy + secOuterR * sinf((float)nextA)), arcCol, 4.0f);
                    // Inner arc
                    dl->AddLine(ImVec2(cx + secInnerR * cosf((float)a), cy + secInnerR * sinf((float)a)),
                                ImVec2(cx + secInnerR * cosf((float)nextA), cy + secInnerR * sinf((float)nextA)), arcCol, 4.0f);
                }
                // Cap lines
                dl->AddLine(ImVec2(cx + secInnerR * cosf((float)radA1), cy + secInnerR * sinf((float)radA1)),
                            ImVec2(cx + secOuterR * cosf((float)radA1), cy + secOuterR * sinf((float)radA1)), arcCol, 4.0f);
                dl->AddLine(ImVec2(cx + secInnerR * cosf((float)radA2), cy + secInnerR * sinf((float)radA2)),
                            ImVec2(cx + secOuterR * cosf((float)radA2), cy + secOuterR * sinf((float)radA2)), arcCol, 4.0f);
            }
        }

        // === Outer ring for Groups ===
        dl->AddCircle(ImVec2(cx, cy), groupR, (C_LINE & 0x00FFFFFF) | (215 << 24), 64, 2.0f);
        dl->AddCircle(ImVec2(cx, cy), groupR + 1.5f, (C_GOLD & 0x00FFFFFF) | (75 << 24), 64, 2.2f);
        dl->AddCircle(ImVec2(cx, cy), groupR + 4.0f, (C_GOLD & 0x00FFFFFF) | (24 << 24), 64, 3.0f);

        // === Inner ring ===
        dl->AddCircle(ImVec2(cx, cy), innerR, (C_GOLD & 0x00FFFFFF) | (140 << 24), 32, 2.0f);
        dl->AddCircle(ImVec2(cx, cy), innerR - 1.0f, (C_GOLD & 0x00FFFFFF) | (28 << 24), 32, 3.0f);

        // === Center hub ===
        dl->AddCircleFilled(ImVec2(cx, cy), innerR, (C_BOX & 0x00FFFFFF) | (248 << 24));
        dl->AddCircleFilled(ImVec2(cx, cy), innerR - 6.0f, (C_DARK & 0x00FFFFFF) | (170 << 24));
        dl->AddCircle(ImVec2(cx, cy), innerR, (C_GOLD & 0x00FFFFFF) | (160 << 24), 32, 2.3f);
        dl->AddCircle(ImVec2(cx, cy), innerR - 4.0f, (C_GOLD & 0x00FFFFFF) | (80 << 24), 32, 1.2f);
        dl->AddCircleFilled(ImVec2(cx, cy), 16.0f, (C_GOLD & 0x00FFFFFF) | (24 << 24));

        const char* centerTxt = "D";
        float dSize = 35.0f;
        ImVec2 cts = fontSegoeBold20->CalcTextSizeA(dSize, FLT_MAX, 0.0f, centerTxt);
        float textX = cx - cts.x * 0.5f + 1.0f;
        float textY = cy - cts.y * 0.5f - 2.0f;
        dl->AddText(fontSegoeBold20, dSize, ImVec2(textX + 1.0f, textY + 1.0f), IM_COL32(0, 0, 0, 185), centerTxt);
        dl->AddText(fontSegoeBold20, dSize, ImVec2(textX, textY), C_GOLD, centerTxt);
        return;
    }

    // ==========================================
    // Standard Mode (fallback below)
    // ==========================================
    float R = 150.0f, innerR = 42.0f;
    int n = radialSectorCount;
    if (n < 2) n = 2;

    double angleStep = 360.0 / n;
    double startOffset = -90.0 - (angleStep / 2.0);

    if (dist > innerR) {
        double normAngle = mouseAngle - startOffset;
        while (normAngle < 0) normAngle += 360.0;
        while (normAngle >= 360.0) normAngle -= 360.0;
        radialHoveredSector = (int)(normAngle / angleStep);
        if (radialHoveredSector >= n) radialHoveredSector = n - 1;
    }

    for (int i = 0; i < n; i++) {
        double a1 = (startOffset + i * angleStep) * 3.14159 / 180.0;
        double a2 = (startOffset + (i + 1) * angleStep) * 3.14159 / 180.0;
        double midA = (a1 + a2) / 2.0;
        bool hovered = (i == radialHoveredSector);

        ImU32 sectorColor = hovered ? ((C_GOLD & 0x00FFFFFF) | (68 << 24)) : themeBg;
        ImU32 dividerColor = hovered ? ((C_GOLD & 0x00FFFFFF) | (230 << 24)) : ((C_LINE & 0x00FFFFFF) | (210 << 24));

        const int steps = 24;
        ImVec2 outerPts[steps + 1];
        ImVec2 innerPts[steps + 1];
        for (int s = 0; s <= steps; s++) {
            double a = a1 + (a2 - a1) * s / steps;
            float ca = cosf((float)a), sa = sinf((float)a);
            outerPts[s] = ImVec2(cx + R * ca, cy + R * sa);
            innerPts[s] = ImVec2(cx + innerR * ca, cy + innerR * sa);
        }
        auto sectorOldFlags = dl->Flags;
        dl->Flags &= ~ImDrawListFlags_AntiAliasedFill;
        for (int s = 0; s < steps; s++) {
            ImVec2 quad[4] = { outerPts[s], outerPts[s+1], innerPts[s+1], innerPts[s] };
            dl->AddConvexPolyFilled(quad, 4, sectorColor);
        }
        if (hovered) {
            for (int s = 0; s < steps; s++) {
                ImVec2 quad[4] = { outerPts[s], outerPts[s+1], innerPts[s+1], innerPts[s] };
                dl->AddConvexPolyFilled(quad, 4, glowCol);
            }
        }
        dl->Flags = sectorOldFlags;

        ImVec2 lineStart(cx + innerR * cosf((float)a1), cy + innerR * sinf((float)a1));
        ImVec2 lineEnd(cx + R * cosf((float)a1), cy + R * sinf((float)a1));
        dl->AddLine(lineStart, lineEnd, dividerColor, hovered ? 2.5f : 2.0f);

        float iconR = (R + innerR) * 0.5f;
        float ix = cx + iconR * cosf((float)midA);
        float iy = cy + iconR * sinf((float)midA);

        bool hasBind = (i < (int)radialSectors.size() && !radialSectors[i].bindId.empty());
        if (hasBind) {
            float textSizePx = 14.0f;
            float wrapWidth = 102.0f;
            if (n <= 4) wrapWidth = 118.0f;
            else if (n >= 7) wrapWidth = 98.0f;
            ImU32 textCol = hovered ? C_WHITE : IM_COL32(255, 255, 255, 220);
            ImU32 textShadow = IM_COL32(0, 0, 0, hovered ? 200 : 160);
            ImU32 iconCol = hovered ? C_GOLD : ((C_GOLD & 0x00FFFFFF) | (180 << 24));

            const std::string& bindNameRef = radialSectors[i].bindName;
            std::vector<std::string> words;
            {
                size_t pos = 0;
                while (pos < bindNameRef.size()) {
                    while (pos < bindNameRef.size() && bindNameRef[pos] == ' ') ++pos;
                    if (pos >= bindNameRef.size()) break;
                    size_t next = bindNameRef.find(' ', pos);
                    if (next == std::string::npos) next = bindNameRef.size();
                    words.emplace_back(bindNameRef.substr(pos, next - pos));
                    pos = next;
                }
            }

            std::vector<std::string> wrappedLines;
            if (words.size() >= 2) {
                size_t split = (words.size() + 1) / 2;
                std::string l1, l2;
                for (size_t wi = 0; wi < split; ++wi) {
                    if (!l1.empty()) l1 += " ";
                    l1 += words[wi];
                }
                for (size_t wi = split; wi < words.size(); ++wi) {
                    if (!l2.empty()) l2 += " ";
                    l2 += words[wi];
                }
                wrappedLines.push_back(l1);
                if (!l2.empty()) wrappedLines.push_back(l2);
            } else {
                wrappedLines.push_back(bindNameRef);
            }

            for (int step = 0; step < 3; ++step) {
                bool fits = true;
                for (const auto& line : wrappedLines) {
                    ImVec2 s = fontSegoeBold14->CalcTextSizeA(textSizePx, FLT_MAX, 0.0f, line.c_str());
                    if (s.x > wrapWidth) { fits = false; break; }
                }
                if (fits) break;
                textSizePx -= 1.0f;
            }
            if (textSizePx < 9.5f) textSizePx = 9.5f;

            const float lineH = textSizePx + 2.0f;
            const float totalH = lineH * (float)wrappedLines.size();
            const float baseY = iy - 12.0f - totalH * 0.5f;
            ImVec4 clipRect(ix - wrapWidth * 0.5f, baseY - 2.0f, ix + wrapWidth * 0.5f, baseY + totalH + 2.0f);
            for (size_t li = 0; li < wrappedLines.size(); ++li) {
                const std::string& line = wrappedLines[li];
                ImVec2 lineSize = fontSegoeBold14->CalcTextSizeA(textSizePx, FLT_MAX, 0.0f, line.c_str());
                float lineY = baseY + (float)li * lineH;
                dl->AddText(fontSegoeBold14, textSizePx, ImVec2(ix - lineSize.x * 0.5f + 1.0f, lineY + 1.0f), textShadow, line.c_str(), nullptr, 0.0f, &clipRect);
                dl->AddText(fontSegoeBold14, textSizePx, ImVec2(ix - lineSize.x * 0.5f, lineY), textCol, line.c_str(), nullptr, 0.0f, &clipRect);
            }
            DrawRadialIcon(dl, ImVec2(ix, iy + 14.0f), 19.0f, radialSectors[i].icon, iconCol);
        } else {
            char numBuf[4]; sprintf_s(numBuf, "%d", i + 1);
            ImVec2 ns = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, numBuf);
            ImU32 numCol = hovered ? C_GOLD : ((C_GRAY & 0x00FFFFFF) | (200 << 24));
            dl->AddText(fontSegoeBold14, 14.0f, ImVec2(ix - ns.x * 0.5f, iy - ns.y * 0.5f), numCol, numBuf);
        }
    }

    if (radialHoveredSector >= 0 && radialHoveredSector < n) {
        double a1 = (startOffset + radialHoveredSector * angleStep) * 3.14159 / 180.0;
        double a2 = (startOffset + (radialHoveredSector + 1) * angleStep) * 3.14159 / 180.0;
        ImVec2 lineStart1(cx + innerR * cosf((float)a1), cy + innerR * sinf((float)a1));
        ImVec2 lineEnd1(cx + R * cosf((float)a1), cy + R * sinf((float)a1));
        ImVec2 lineStart2(cx + innerR * cosf((float)a2), cy + innerR * sinf((float)a2));
        ImVec2 lineEnd2(cx + R * cosf((float)a2), cy + R * sinf((float)a2));
        dl->AddLine(lineStart1, lineEnd1, (C_GOLD & 0x00FFFFFF) | (245 << 24), 2.5f);
        dl->AddLine(lineStart2, lineEnd2, (C_GOLD & 0x00FFFFFF) | (245 << 24), 2.5f);
    }

    dl->AddCircle(ImVec2(cx, cy), R, (C_LINE & 0x00FFFFFF) | (215 << 24), 64, 2.0f);
    dl->AddCircle(ImVec2(cx, cy), R + 1.5f, (C_GOLD & 0x00FFFFFF) | (75 << 24), 64, 2.2f);
    dl->AddCircle(ImVec2(cx, cy), R + 4.0f, (C_GOLD & 0x00FFFFFF) | (24 << 24), 64, 3.0f);
    dl->AddCircle(ImVec2(cx, cy), innerR, (C_GOLD & 0x00FFFFFF) | (140 << 24), 32, 2.0f);
    dl->AddCircle(ImVec2(cx, cy), innerR - 1.0f, (C_GOLD & 0x00FFFFFF) | (28 << 24), 32, 3.0f);
    dl->AddCircleFilled(ImVec2(cx, cy), innerR, (C_BOX & 0x00FFFFFF) | (248 << 24));
    dl->AddCircleFilled(ImVec2(cx, cy), innerR - 6.0f, (C_DARK & 0x00FFFFFF) | (170 << 24));
    dl->AddCircle(ImVec2(cx, cy), innerR, (C_GOLD & 0x00FFFFFF) | (160 << 24), 32, 2.3f);
    dl->AddCircle(ImVec2(cx, cy), innerR - 4.0f, (C_GOLD & 0x00FFFFFF) | (80 << 24), 32, 1.2f);
    dl->AddCircleFilled(ImVec2(cx, cy), 16.0f, (C_GOLD & 0x00FFFFFF) | (24 << 24));

    const char* centerTxt = "D";
    float dSize = 35.0f;
    ImVec2 cts = fontSegoeBold20->CalcTextSizeA(dSize, FLT_MAX, 0.0f, centerTxt);
    float textX = cx - cts.x * 0.5f + 1.0f;
    float textY = cy - cts.y * 0.5f - 2.0f;
    dl->AddText(fontSegoeBold20, dSize, ImVec2(textX + 1.0f, textY + 1.0f), IM_COL32(0, 0, 0, 185), centerTxt);
    dl->AddText(fontSegoeBold20, dSize, ImVec2(textX, textY), C_GOLD, centerTxt);
}

void Gui::RenderRadialIdInput() {
    ImDrawList* dl = ImGui::GetForegroundDrawList();
    ImVec2 ds = ImGui::GetIO().DisplaySize;
    float cx = ds.x * 0.5f, cy = ds.y * 0.5f;

    // Window dimensions
    float boxW = 220.0f;
    float boxH = 95.0f;
    float bx = cx - boxW * 0.5f;
    float by = cy - boxH * 0.5f;

    // Box background
    dl->AddRectFilled(ImVec2(bx, by), ImVec2(bx + boxW, by + boxH), (C_BOX & 0x00FFFFFF) | (240 << 24), 8.0f);
    dl->AddRect(ImVec2(bx, by), ImVec2(bx + boxW, by + boxH), (C_LINE & 0x00FFFFFF) | (200 << 24), 8.0f, 0, 2.0f);

    // Title
    const char* title = "\xD0\x92\xD0\x92\xD0\x95\xD0\x94\xD0\x98\xD0\xA2\xD0\x95 ID \xD0\x98\xD0\x93\xD0\xA0\xD0\x9E\xD0\x9A\xD0\x90";
    ImVec2 ts = fontSegoeBold14->CalcTextSizeA(14.0f, FLT_MAX, 0.0f, title);
    dl->AddText(fontSegoeBold14, 14.0f, ImVec2(cx - ts.x * 0.5f, by + 12.0f), C_WHITE, title);

    // Input field
    float fieldW = 80.0f;
    float fieldH = 30.0f;
    float fx = cx - fieldW * 0.5f;
    float fy = by + 35.0f;

    dl->AddRectFilled(ImVec2(fx, fy), ImVec2(fx + fieldW, fy + fieldH), (C_DARK & 0x00FFFFFF) | (255 << 24), 4.0f);
    dl->AddRect(ImVec2(fx, fy), ImVec2(fx + fieldW, fy + fieldH), (C_GOLD & 0x00FFFFFF) | (200 << 24), 4.0f, 0, 1.5f);

    std::string text = radialIdBuffer;
    ImVec2 inputTs = fontSegoeBold14->CalcTextSizeA(16.0f, FLT_MAX, 0.0f, text.c_str());
    dl->AddText(fontSegoeBold14, 16.0f, ImVec2(cx - inputTs.x * 0.5f, fy + (fieldH - inputTs.y) * 0.5f), C_WHITE, text.c_str());

    // Help text (inside the box)
    const char* help = "ENTER - \xD0\xBF\xD0\xBE\xD0\xB4\xD1\x82\xD0\xB2\xD0\xB5\xD1\x80\xD0\xB4\xD0\xB8\xD1\x82\xD1\x8C  |  ESC - \xD0\xBE\xD1\x82\xD0\xBC\xD0\xB5\xD0\xBD\xD0\xB0";
    ImVec2 hs = fontSegoeBold12->CalcTextSizeA(11.0f, FLT_MAX, 0.0f, help);
    dl->AddText(fontSegoeBold12, 11.0f, ImVec2(cx - hs.x * 0.5f, by + boxH - 22.0f), (C_WHITE & 0x00FFFFFF) | (150 << 24), help);
}