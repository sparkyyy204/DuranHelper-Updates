#pragma once
#include <string>
#include <vector>
#include <unordered_map>
#include <windows.h>
#include "libs/json/json.hpp"

using json = nlohmann::ordered_json;

// Helper: Get Settings/Profiles path from AppData\Roaming\DuranHelper
std::string GetAppDataPath();

struct BindStep {
    std::string action; // "text", "delay", "command"
    std::string value; // e.g. "Здравия желаю!" or "1500"
    bool isEnter = true;
};

struct BindItem {
    std::string id;
    std::string name;
    std::string ahkKey; // e.g. "^F9" or "Numpad1"
    std::string Group;  // bind group name
    
    // Parsed Windows KeyInfo
    int vkCode = 0;
    bool needsAlt = false;
    bool needsCtrl = false;
    bool needsShift = false;

    bool active = true;
    bool isAuto = false;       // Auto-replace trigger mode
    std::string autoTrigger;   // Trigger text (e.g. "МО1")
    std::vector<BindStep> steps;

    // Checks if current keyboard state matches this bind
    bool Matches(int vkPressed) const;

    // Returns human-readable key combo string like "ALT + Q"
    std::string KeyComboStr() const;
};

class BinderManager {
public:
    static BinderManager& Get() {
        static BinderManager instance;
        return instance;
    }

    // Loads AppData/Roaming/DuranHelper/Settings.json -> Profiles.json
    void ReloadBinds();

    // Find bind by key press
    BindItem* FindBind(int vkPressed);

    // Execute a bind (used by overlay play button)
    void ExecuteBind(const BindItem& bind);

    // Map key name string to VK code (public so gui.cpp can use it too)
    static int StringToVK(const std::string& keyStr);

    // List of active binds
    std::vector<BindItem> Binds;

    // Variables for substitution (e.g. "*ТЕГ*" -> "[ПП | Дуран]")
    std::map<std::string, std::string> Variables;

    // Helper to map AHK string to VK code
    void ParseAhkKey(BindItem& bind);

private:
    BinderManager() = default;
};
