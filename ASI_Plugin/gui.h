#pragma once
#include <d3d9.h>
#include <string>
#include <vector>
#include "imgui.h"
#include "imgui_impl_dx9.h"
#include "imgui_impl_win32.h"

// ===== Law Structures =====
struct LawItem {
    std::string type; // "head" or "art"
    std::string id;
    std::string txt;
    std::string pun; 
    int level;
};

// ===== Fine/Ticket Structures =====
struct FineItem {
    std::string id;       // "[1.1]"
    std::string type;     // "УК РФ" or "КоАП"
    std::string name;     // "Езда без гос. знаков"
    int amount;           // 8000
    bool hasLicRevoke;    // лишение ВУ
    bool selected;        // checkbox state
};

// ===== Law Section =====
struct NoteSegment {
    std::string text;
    ImU32 color;
    bool bold;
    bool italic;
    bool underline;
    int alignment; // 0=left, 1=center, 2=right
};

struct LawSection {
    std::string name;     // "Уголовный Кодекс (УК)"
    std::string key;      // JSON key
    std::string type;     // "laws", "text", "1col", "2col"
    std::string content;  // Plain text fallback
    std::string rtfData;  // Raw RTF string from JSON
    std::vector<NoteSegment> noteSegments; // Parsed RTF chunks for rendering
    bool hasPunishments;
    std::vector<LawItem> items;
};

// ===== Player Database (kept for stub) =====
struct HistoryEntry {
    std::string date;
    std::string type;
    std::string text;
};

struct PlayerRecord {
    std::string nick;
    std::vector<std::string> tags;
    std::string notes;
    std::vector<HistoryEntry> history;
};

// ===== Radial Menu =====
struct RadialSector {
    std::string bindId;
    std::string bindName;
    std::string icon;        // "star", "megaphone", "handcuffs", "lightning", "document", "car", "badge", "radio"
    bool requiresId = false;
};

struct RadialMenuGroup {
    std::string name;
    int sectorCount;
    std::vector<RadialSector> sectors;
};

class Gui {
public:
    static bool show;
    static bool clearNextFrame;
        
    static float alpha;
    static int activeTab;            // 0=Законы, 1=Штрафы, 2=Биндер, 3=База
    
    // Version
    static std::string versionStr;

    // Fonts
    static ImFont* fontArialBlack24;
    static ImFont* fontSegoeBold12;
    static ImFont* fontSegoeBold14;
    static ImFont* fontSegoeBold20;
    static ImFont* fontSegoeBlack32;

    // Laws
    static std::vector<LawSection> lawSections;
    static int selectedLawSection;
    static bool showLawDropdown;
    static bool resetLawsScroll;
    static char searchLaws[256];
    static float lawScrollY;

    // Fines
    static std::vector<FineItem> fineItems;
    static char searchFines[256];
    static char fineIdBuf[32];
    static bool fineWithRevoke;
    static float fineScrollY;

    // Binder
    static int selectedBindGroup;    // -1 = all
    static char searchBinder[256];
    static float binderScrollY;
    static float globalScale;         // scaling factor for different resolutions
    static bool showSettings;         // settings gear panel
    static float settingsAlpha;       // transparency slider value
    static int toggleKey;             // Virtual-key code for overlay toggle
    static std::string toggleKeyStr;
    static bool toggleNeedsAlt;
    static bool toggleNeedsCtrl;
    static bool toggleNeedsShift;
    static int currentTheme;          // 0 = Default, 1 = Black, 2 = Grey
    static int binderDelay;            // ms between bind steps (0-1000, step 200)
    static bool rememberTab;           // remember last active tab
    static bool searchCurrentSection;   // search only in selected section
    
    // Smart Quoting
    static bool quoteEnabled;
    static bool quoteExtended;
    static bool quoteChapter;
    static bool quoteFines;
    
    // Error notification
    static std::string overlayErrorMsg;
    static ImU32 overlayErrorColor;
    static float overlayErrorTimer;

    // Database (stub)
    static std::vector<PlayerRecord> playerDb;

    // Radial Menu
    static bool radialMenuOpen;
    static bool radialEnabled;
    static std::string radialMode;        // "Standard" | "Grouped"
    static int radialSectorCount;
    static std::vector<RadialSector> radialSectors;
    static int radialGroupCount;
    static std::vector<RadialMenuGroup> radialGroups;
    static int radialSelectedGroup;       // -1 = home ring
    static int radialHoveredGroup;        // -1 = none
    static int radialHoveredSector;       // -1 = none
    static bool radialIdInputOpen;
    static char radialIdBuffer[32];
    static int radialIdTargetSector;      // sector that triggered ID input
    static bool radialIdFocusRequest;     // request keyboard focus on next frame
    static bool radialJustOpened;
    
    static void Init(IDirect3DDevice9* pDevice);
    static void Render();
    static void Toggle();
    static void LoadLaws();
    static void LoadFines();
    static void LoadVersion();
    static void LoadSettings();
    static void SaveSettings();
    static void LoadRadialConfig();
    static void ShowError(const std::string& msg, ImU32 color);
    static void ApplyTheme();
    static void ExecuteLawQuote(const std::string& utf8text);

private:
    static void SetupStyle();
    static void DrawHudFrame(ImDrawList* dl, ImVec2 origin);
    static void DrawTabs(ImDrawList* dl, ImVec2 origin);
    static void RenderLawsTab(ImDrawList* dl, ImVec2 origin);
    static void RenderFinesTab(ImDrawList* dl, ImVec2 origin);
    static void RenderBinderTab(ImDrawList* dl, ImVec2 origin);
    static void RenderSettingsTab(ImDrawList* dl, ImVec2 origin);
    static void RenderDatabaseStub(ImDrawList* dl, ImVec2 origin);
    static void RenderRadialMenu();
    static void RenderRadialIdInput();
    static void DrawRadialIcon(ImDrawList* dl, ImVec2 center, float size, const std::string& icon, ImU32 color);
    static std::string GetCurrentDateTime();
};

