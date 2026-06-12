#pragma once
#include <d3d9.h>
#include <string>
#include <vector>
#include <map>
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

struct WantedItem {
    std::string id;       // "[1.1]"
    std::string type;     // "УК"
    std::string name;     // text
    int stars;            // 2
    std::string note;
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

// ===== Notification System =====
struct Notification {
    std::string type;
    std::string text;
    std::string keyAccept;
    std::string actionAccept;
    std::string keyCancel;
    std::string actionCancel;
    float duration;
    float maxDuration;
    bool hasProgress;
    ImU32 color;
    bool isPayday;
    ULONGLONG startTick;
};

extern float savedSensX, savedSensY;

struct PatrolData {
    std::string name;
    std::string startText;
    std::string processText;
    std::string endText;
    int defaultIntervalMin;
};

struct ActivePatrol {
    bool active;
    PatrolData data;
    int currentStage; // 0=start, 1=process, 2=end
    float timeRemainingSec;
    int intervalMin;
    int totalMin;
    float totalElapsedSec;
    bool autoSend;
    std::map<std::string, std::string> variables;
    int reportsSent;
    ULONGLONG lastTickTime;
};

class Gui {
public:
    static bool show;
    static bool showBinderHint;
    static bool clearNextFrame;
        
    static float alpha;
    static int activeTab;            // -1=Меню, 0=Законы, 1=Штрафы, 2=Биндер, 3=База, 4=Розыск, 5=Патрули
    
    // UI Modes
    static bool useGridMenu;

    // Temporary edit variables for patrol UI
    static std::map<std::string, std::string> editVariables;

    // Version
    static std::string versionStr;

    // Fonts
    static ImFont* fontArialBlack24;
    static ImFont* fontSegoeBold12;
    static ImFont* fontSegoeBold14;
    static ImFont* fontSegoeBold20;
    static ImFont* fontSegoeBlack32;
    
    // Notepad specific fonts
    static ImFont* fontSegoeRegular13;
    static ImFont* fontSegoeItalic13;
    static ImFont* fontSegoeBoldItalic14;

    // Patrols
    static std::vector<PatrolData> patrols;
    static ActivePatrol activePatrol;
    static int selectedPatrolIndex;

    // Laws
    static std::vector<LawSection> lawSections;
    static int selectedLawSection;
    static bool showLawDropdown;
    static bool resetLawsScroll;
    static char searchLaws[256];
    static bool showSearchPopup;
    static char searchBufferPopup[256];
    static int searchMatchIndex;
    static int searchMatchCount;
    static bool searchPopupFocus;
    static bool scrollSearch;
    static float lawScrollY;

    // Fines
    static std::vector<FineItem> fineItems;
    static char searchFines[256];
    static char fineIdBuf[32];
    static bool fineWithRevoke;
    static float fineScrollY;

    // Wanted
    static std::vector<WantedItem> wantedItems;
    static char searchWanted[256];
    static char wantedIdBuf[32];
    static float wantedScrollY;
    
    // Binder
    static int selectedBindGroup;    // -1 = all
    static char searchBinder[256];
    static float binderScrollY;
    static float globalScale;         // scaling factor for different resolutions
    static bool showSettings;         // settings gear panel
    static float settingsAlpha;
    static bool scriptEnabled;
    static bool binderEnabled;
    static std::string scriptToggleKeyStr;
    static int scriptToggleKey;
    static bool scriptToggleNeedsAlt;
    static bool scriptToggleNeedsCtrl;
    static bool scriptToggleNeedsShift;
    static bool radialActivationToggleMode;       // transparency slider value
    static int toggleKey;             // Virtual-key code for overlay toggle
    static std::string toggleKeyStr;
    static bool toggleNeedsAlt;
    static bool toggleNeedsCtrl;
    static bool toggleNeedsShift;

    // --- Window State & Features ---
    static bool windowDraggable;
    static float overlayPosX;
    static float overlayPosY;
    static bool blockKeyboardInput;
    
    // --- Tab Visibility ---
    static bool showTabBinder;
    static bool showTabFines;
    static bool showTabLaws;
    static bool showTabWanted;

    static int binderHintKey;
    static std::string binderHintKeyStr;
    static bool binderHintNeedsAlt;
    static bool binderHintNeedsCtrl;
    static bool binderHintNeedsShift;

    static int issueFineKey;
    static std::string issueFineKeyStr;
    static bool issueFineNeedsAlt;
    static bool issueFineNeedsCtrl;
    static bool issueFineNeedsShift;

    static int cancelFineKey;
    static std::string cancelFineKeyStr;
    static bool cancelFineNeedsAlt;
    static bool cancelFineNeedsCtrl;
    static bool cancelFineNeedsShift;

    static int stopBindKey;              // Key to abort a running bind sequence (0 = disabled)
    static inline std::string stopBindKeyStr = "";
    static inline bool stopBindNeedsAlt = false;
    static inline bool stopBindNeedsCtrl = false;
    static inline bool stopBindNeedsShift = false;

    static int currentTheme;          // 0 = Default, 1 = Black, 2 = Grey
    static int binderDelay;            // ms between bind steps (0-1000, step 200)
    static bool rememberTab;           // remember last active tab
    static bool searchCurrentSection;   // search only in selected section
    static bool clearSearchOnClose;
    static bool closeOnClickOutside;
    static ULONGLONG lastCloseTime;
    
    // Smart Quoting
    static bool quoteEnabled;
    static bool quoteNotepad;
    static bool quoteExtended;
    static bool quoteChapter;
    static bool quoteFines;
    static bool quoteWanted;
    
    // Notifications
    static bool notifyFineIssue;
    static bool notifyPayday;
    
    // Error notification
    static std::string overlayErrorMsg;
    static ImU32 overlayErrorColor;
    static float overlayErrorTimer;

    // Database
    static std::vector<PlayerRecord> playerDb;
    static int selectedDbPlayer;
    static char searchDatabase[256];

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
    static void RenderBinderHint();
    static void Toggle();
    static void HandleEscape();
    static void ToggleBinderHint();
    static void LoadLaws();
    static void LoadFines();
    static void LoadWanted();
    static void LoadVersion();
    static void LoadSettings();
    static void SaveSettings();
    static void LoadRadialConfig();
    static void ShowError(const std::string& msg, ImU32 color);
    static void AddNotification(const std::string& type, const std::string& text, const std::string& keyAccept, const std::string& actionAccept, const std::string& keyCancel, const std::string& actionCancel, float maxDuration, bool hasProgress, ImU32 color);
    static void ApplyTheme(float alphaMul = 1.0f);
    static void ExecuteLawQuote(const std::string& utf8text);
    static void ExecutePatrolReport();
    static void ClearNotifications();

private:
    static std::vector<Notification> activeNotifications;
    static void SetupStyle();
    static void RenderNotifications();
    static void DrawHudFrame(ImDrawList* dl, ImVec2 origin);
    static void DrawTabs(ImDrawList* dl, ImVec2 origin);
    static void DrawGridMenu(ImDrawList* dl, ImVec2 origin);
    static void DrawGridBackButton(ImDrawList* dl, ImVec2 origin);
    static void RenderLawsTab(ImDrawList* dl, ImVec2 origin);
    static void RenderFinesTab(ImDrawList* dl, ImVec2 origin);
    static void RenderBinderTab(ImDrawList* dl, ImVec2 origin);
    static void RenderWantedTab(ImDrawList* dl, ImVec2 origin);
    static void RenderSettingsTab(ImDrawList* dl, ImVec2 origin);
    static void RenderDatabaseTab(ImDrawList* dl, ImVec2 origin);
    static void RenderPatrolsTab(ImDrawList* dl, ImVec2 origin);
    static void RenderPatrolWidget();
    static void RenderRadialMenu();
    static void RenderRadialIdInput();
    static void DrawRadialIcon(ImDrawList* dl, ImVec2 center, float size, const std::string& icon, ImU32 color);
    static std::string GetCurrentDateTime();
};

