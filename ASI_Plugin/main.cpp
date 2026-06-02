#include <windows.h>
#include <windowsx.h>
#include <d3d9.h>
#include <optional>
#include "gui.h"
#pragma warning(push)
#pragma warning(disable : 26859) // Empty optional
#pragma warning(disable : 26813) // Bitwise AND
#pragma warning(disable : 26819) // Switch fallthrough (es.78)
#include "kthook/kthook.hpp"
#pragma warning(pop)
#include "RakNet/RakClientInterface.h"
#include <thread>
#include <chrono>
#include <atomic>
#include "binder.h"

#pragma comment(lib, "d3d9.lib")

HMODULE g_hModule = nullptr;
HWND g_hWnd = nullptr;
WNDPROC oWndProc = nullptr;
bool g_WndProcHooked = false;
#include <fstream>

bool g_Initialized = false;
std::atomic<bool> g_CancelQuote(false);
std::atomic<bool> g_QuotingActive(false);
std::atomic<bool> g_CancelBind(false);  // Stop running binder sequence

// ===== Function signatures =====
using EndScene_t = HRESULT(__stdcall*)(IDirect3DDevice9*);
using Reset_t = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);
using SetCursorPos_t = BOOL(WINAPI*)(int, int);

// ===== kthook instances (global) =====
kthook::kthook_signal<SetCursorPos_t> hookSetCursorPos;
kthook::kthook_simple<void(__cdecl*)()> hookCPadUpdate{0x541DD0};

typedef SHORT(WINAPI* GetAsyncKeyState_t)(int);
kthook::kthook_signal<GetAsyncKeyState_t> hookGetAsyncKeyState;

typedef BOOL(WINAPI* GetKeyboardState_t)(PBYTE);
kthook::kthook_signal<GetKeyboardState_t> hookGetKeyboardState;

typedef LRESULT(WINAPI* DispatchMessageA_t)(const MSG*);
kthook::kthook_signal<DispatchMessageA_t> hookDispatchMessageA;

typedef LRESULT(WINAPI* DispatchMessageW_t)(const MSG*);
kthook::kthook_signal<DispatchMessageW_t> hookDispatchMessageW;

typedef void(__thiscall* CChat_AddMessage_t)(DWORD, D3DCOLOR, const char*);
kthook::kthook_simple<CChat_AddMessage_t> hookCChatAddMessage;

#include <string>
#include <mutex>
#include <queue>
#include <functional>

std::mutex g_TaskMutex;
std::queue<std::function<void()>> g_TaskQueue;

// Chat welcome state — shared across DispatchMessageA and W hooks
static bool g_chatWelcomeSent = false;
static ULONGLONG g_chatReadyTime = 0;

static void Log(const std::string& text) {
    /*
    std::ofstream ofs("DuranOverlay_Debug.log", std::ios::app);
    if (ofs.is_open()) {
        ofs << text << "\n";
        ofs.close();
    }
    */
}
extern LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// Helper to convert UTF-8 strings (Visual Studio / ImGui) to CP1251 (SA-MP Cyrillic)
std::string UTF8ToCP1251(const char* utf8) {
    if (!utf8 || !*utf8) return "";
    
    // UTF-8 -> UTF-16
    int wlen = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, NULL, 0);
    if (wlen <= 0) return "";
    std::wstring wstr(wlen, 0);
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, &wstr[0], wlen);
    
    // UTF-16 -> Windows-1251
    int clen = WideCharToMultiByte(1251, 0, wstr.c_str(), -1, NULL, 0, NULL, NULL);
    if (clen <= 0) return "";
    std::string res(clen, 0);
    WideCharToMultiByte(1251, 0, wstr.c_str(), -1, &res[0], clen, NULL, NULL);
    
    res.resize(clen - 1); // remove null terminator for std::string compatibility
    return res;
}

// Forward declare
extern bool InterceptFineCommand(const std::string& cmd);

// Helper to send chat directly via RakNet, bypassing SAMP CInput hook
void SendSAMPMessage(const char* msg) {
    if (!msg || msg[0] == '\0') {
        Log("SendSAMPMessage: Message is empty");
        return;
    }
    
    // Intercept /go and /stop for fine sequence
    if (msg[0] == '/') {
        std::string cmdStr(msg);
        if (InterceptFineCommand(cmdStr)) {
            Log("SendSAMPMessage: Intercepted fine command: " + cmdStr);
            return;
        }
    }
    
    HMODULE hSamp = GetModuleHandleA("samp.dll");
    if (!hSamp) {
        Log("SendSAMPMessage: samp.dll not found");
        return;
    }
    
    DWORD pNetGamePtr = (DWORD)((DWORD)hSamp + 0x26E8DC); // 0.3.7-R3-1 offset
    if (!pNetGamePtr || !*(DWORD*)pNetGamePtr) {
        Log("SendSAMPMessage: pNetGamePtr is NULL");
        return;
    }
    
    DWORD pNetGame = *(DWORD*)pNetGamePtr;
    RakClientInterface** ppRakClient = (RakClientInterface**)(pNetGame + 0x2C);
    if (!ppRakClient || !*ppRakClient) {
        Log("SendSAMPMessage: ppRakClient is NULL");
        return;
    }
    
    RakClientInterface* pRakClient = *ppRakClient;
    RakNet::BitStream bs;
    
    // Message should ALREADY be Windows-1251 at this point!
    std::string encodedStr(msg);
    
    if (encodedStr[0] == '/') {
        // Command (RPC 50)
        UINT32 len = (UINT32)encodedStr.length();
        bs.Write(len);
        bs.Write(encodedStr.c_str(), len);
        
        int rpc_id = 50; 
        Log("SendSAMPMessage: Triggering RPC 50 Command. Length: " + std::to_string(len) + " Text: " + encodedStr);
        pRakClient->RPC(&rpc_id, &bs, HIGH_PRIORITY, RELIABLE, 0, false);
    } else {
        // Chat (RPC 101)
        UINT8 len = (UINT8)encodedStr.length();
        bs.Write(len);
        bs.Write(encodedStr.c_str(), len);
        
        int rpc_id = 101;
        Log("SendSAMPMessage: Triggering RPC 101 Chat. Length: " + std::to_string(len) + " Text: " + encodedStr);
        pRakClient->RPC(&rpc_id, &bs, HIGH_PRIORITY, RELIABLE, 0, false);
    }
    
    Log("SendSAMPMessage: RPC dispatch completed without crash!");
}

// Helper to print local message in SAMP chat
static void AddLocalSAMPMessage(const char* msg) {
    if (!msg || msg[0] == '\0') return;
    HMODULE hSamp = GetModuleHandleA("samp.dll");
    if (!hSamp) return;

    DWORD sampBase = (DWORD)hSamp;
    DWORD* pChatPtr = (DWORD*)(sampBase + 0x26E8C8); // 0.3.7-R3-1 offset
    if (!pChatPtr || !*pChatPtr) return;

    DWORD pChat = *pChatPtr;
    typedef void(__thiscall* CChat_AddMessage)(DWORD, D3DCOLOR, const char*);
    CChat_AddMessage addMsg = (CChat_AddMessage)(sampBase + 0x679F0);
    
    // We send this as white FFFFFFFF. SA-MP will parse {RRGGBB} codes.
    addMsg(pChat, 0xFFFFFFFF, msg);
}

// Open SA-MP chat input and pre-fill with text using key simulation
void OpenChatWithText(const char* text, int cursorOffset = 0) {
    HWND hwnd = FindWindowA("Grand theft auto San Andreas", NULL);
    if (!hwnd) hwnd = GetForegroundWindow();
    if (!hwnd) { Log("OpenChatWithText: no window"); return; }

    // Press F6 to open chat input (F6 doesn't produce a character like T does)
    PostMessage(hwnd, WM_KEYDOWN, VK_F6, 0x00400001);
    PostMessage(hwnd, WM_KEYUP, VK_F6, 0xC0400001);

    // Small delay then type the text
    Sleep(50);

    // Type each character via WM_CHAR
    if (text) {
        for (int i = 0; text[i]; i++) {
            PostMessage(hwnd, WM_CHAR, (WPARAM)(unsigned char)text[i], 0);
        }
    }

    if (cursorOffset > 0) {
        Sleep(10);
        for (int i = 0; i < cursorOffset; i++) {
            PostMessage(hwnd, WM_KEYDOWN, VK_LEFT, 0);
            PostMessage(hwnd, WM_KEYUP, VK_LEFT, 0);
        }
    }

    Log("OpenChatWithText: typed '" + std::string(text ? text : "") + "' with cursor offset " + std::to_string(cursorOffset));
}

static std::string Utf8ToAnsi(const std::string& utf8Str) {
    if (utf8Str.empty()) return "";
    int wLen = MultiByteToWideChar(CP_UTF8, 0, utf8Str.c_str(), -1, NULL, 0);
    if (wLen <= 0) return utf8Str;
    std::wstring wStr(wLen, 0);
    MultiByteToWideChar(CP_UTF8, 0, utf8Str.c_str(), -1, &wStr[0], wLen);

    int aLen = WideCharToMultiByte(1251, 0, wStr.c_str(), -1, NULL, 0, NULL, NULL);
    if (aLen <= 0) return utf8Str;
    std::string aStr(aLen, 0);
    WideCharToMultiByte(1251, 0, wStr.c_str(), -1, &aStr[0], aLen, NULL, NULL);
    return std::string(aStr.c_str());
}

// Helper to safely execute a task on the main game thread
void RunOnMainThread(std::function<void()> task) {
    std::lock_guard<std::mutex> lock(g_TaskMutex);
    g_TaskQueue.push(task);
    if (g_hWnd) {
        PostMessageA(g_hWnd, WM_APP + 778, 0, 0); // Wake up WndProc
    }
}

// Process all pending tasks in the WndProc hook
static void ProcessMainThreadTasks();

void ExecuteRadialMenuSelection();
static void ProcessMainThreadTasks() {
    std::lock_guard<std::mutex> lock(g_TaskMutex);
    while (!g_TaskQueue.empty()) {
        g_TaskQueue.front()();
        g_TaskQueue.pop();
    }
}

// ===== Fine Post-Processing Sequence (Revoke + Quote) =====
static std::atomic<int> g_FineSequenceState{0}; // 0=idle, 1=waiting_revoke, 2=waiting_quote
static std::atomic<bool> g_FineSequenceGo{false};
static std::atomic<bool> g_FineSequenceStop{false};
static std::atomic<bool> g_FineSequenceCancel{false};

static std::atomic<int> g_WantedSequenceState{0}; // 0=idle, 1=waiting_issue, 2=waiting_quote

static std::string g_ChatBuffer;
static std::atomic<bool> g_ChatOpen{false};
static bool g_SkipNextChar = false;
static std::atomic<bool> g_WantedSequenceGo{false};
static std::atomic<bool> g_WantedSequenceStop{false};
static std::atomic<bool> g_WantedSequenceCancel{false};


// Keep for SendSAMPMessage intercept (our own sends)
bool InterceptFineCommand(const std::string& cmd) {
    if (g_FineSequenceState.load() == 0) return false;
    if (cmd == "/go") { g_FineSequenceGo.store(true); return true; }
    if (cmd == "/stop") { g_FineSequenceStop.store(true); return true; }
    return false;
}

// ===== RPC VTable Hook — intercept /go and /stop from SA-MP chat =====
typedef bool (__thiscall *OrigRPC_t)(void*, int*, RakNet::BitStream*, PacketPriority, PacketReliability, char, bool);
static OrigRPC_t g_OrigRPC = nullptr;

static bool __fastcall HookedRPC(void* pThis, void* /*edx*/, int* uniqueID, RakNet::BitStream* parameters, PacketPriority priority, PacketReliability reliability, char orderingChannel, bool shiftTimestamp) {
    if (g_FineSequenceState.load() != 0 && uniqueID && *uniqueID == 50 && parameters) {
        unsigned char* data = parameters->GetData();
        unsigned int dataLen = parameters->GetNumberOfBytesUsed();
        if (data && dataLen > 4) {
            UINT32 cmdLen = 0;
            memcpy(&cmdLen, data, 4);
            if (cmdLen > 0 && cmdLen < 256 && 4 + cmdLen <= dataLen) {
                std::string cmd((char*)(data + 4), cmdLen);
                if (cmd == "/go") {
                    g_FineSequenceGo.store(true);
                    Log("HookedRPC: intercepted /go");
                    return true;
                }
                if (cmd == "/stop") {
                    g_FineSequenceStop.store(true);
                    Log("HookedRPC: intercepted /stop");
                    return true;
                }

            }
        }
    }
    return g_OrigRPC(pThis, uniqueID, parameters, priority, reliability, orderingChannel, shiftTimestamp);
}

void StartFineSequence(const std::string& targetId, const std::string& articleMsg, bool doRevoke, bool doQuote, const std::string& quoteText) {
    g_FineSequenceCancel.store(false);
    std::thread([id = targetId, art = articleMsg, doRevoke, doQuote, quoteText]() {
        bool revoked = false;
        // --- Phase 1: License Revocation ---
        if (doRevoke) {
            g_FineSequenceState.store(1);
            g_FineSequenceGo.store(false);
            g_FineSequenceStop.store(false);

            std::string k_c = Gui::issueFineKeyStr;
            std::string k_x = Gui::cancelFineKeyStr;
            std::string msgUtf8 = "{D2A65E}[DURAN HELPER] {FFFFFF}\xD0\x9B\xD0\xB8\xD1\x88\xD0\xB8\xD1\x82\xD1\x8C \xD0\x92\xD0\xA3 \xD0\xB8\xD0\xB3\xD1\x80\xD0\xBE\xD0\xBA\xD0\xB0 " + id + "? {D2A65E}" + k_c + " {FFFFFF}- \xD0\xBB\xD0\xB8\xD1\x88\xD0\xB8\xD1\x82\xD1\x8C, {FF6B6B}" + k_x + " {FFFFFF}- \xD0\xBE\xD1\x82\xD0\xBC\xD0\xB5\xD0\xBD\xD0\xB8\xD1\x82\xD1\x8C ({D2A65E}30 \xD1\x81\xD0\xB5\xD0\xBA{FFFFFF})";
            RunOnMainThread([m = UTF8ToCP1251(msgUtf8.c_str()), id, k_c, k_x]() {
                AddLocalSAMPMessage(m.c_str());
                if (Gui::notifyFineIssue) {
                    std::string nMsg = "\xD0\x9B\xD0\x98\xD0\xA8\xD0\x98\xD0\xA2\xD0\xAC \xD0\x92\xD0\xA3 \xD0\x98\xD0\x93\xD0\xA0\xD0\x9E\xD0\x9A\xD0\x90 \xD0\xA1 ID: " + id;
                    Gui::AddNotification("CALCULATOR", nMsg, k_c, "\xD0\xBF\xD1\x80\xD0\xB8\xD0\xBD\xD1\x8F\xD1\x82\xD1\x8C", k_x, "\xD0\xBE\xD1\x82\xD0\xBA\xD0\xBB\xD0\xBE\xD0\xBD\xD0\xB8\xD1\x82\xD1\x8C", 30.0f, true, IM_COL32(239, 68, 68, 255));
                }
            });

            // Wait for Go/Stop signals via WndProc or 30s timeout
            auto start = std::chrono::steady_clock::now();
            bool confirmed = false;
            while (true) {
                if (g_FineSequenceCancel.load()) { g_FineSequenceState.store(0); return; }
                if (g_FineSequenceGo.load()) { confirmed = true; break; }
                if (g_FineSequenceStop.load()) { confirmed = false; break; }
                auto elapsed = std::chrono::steady_clock::now() - start;
                if (std::chrono::duration_cast<std::chrono::seconds>(elapsed).count() >= 30) break;
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }


            // Wait for key release
            RunOnMainThread([]() { Gui::ClearNotifications(); });
            while (((GetAsyncKeyState(Gui::issueFineKey) & 0x8000) != 0) || ((GetAsyncKeyState(Gui::cancelFineKey) & 0x8000) != 0)) {
                if (g_FineSequenceCancel.load()) { g_FineSequenceState.store(0); return; }
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }

            if (confirmed) {
                revoked = true;
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {2EA043}\xD0\x9B\xD0\xB8\xD1\x88\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\x92\xD0\xA3 \xD0\xBF\xD0\xBE\xD0\xB4\xD1\x82\xD0\xB2\xD0\xB5\xD1\\x80\xD0\xB6\xD0\xB4\xD0\xB5\xD0\xBD\xD0\xBE.").c_str());
                });
                std::this_thread::sleep_for(std::chrono::milliseconds(200));

                // Send /takelic
                std::string cmd = "/takelic " + id;
                RunOnMainThread([c = UTF8ToCP1251(cmd.c_str())]() {
                    SendSAMPMessage(c.c_str());
                });

                // Wait for dialog and press DOWN -> ENTER
                std::this_thread::sleep_for(std::chrono::milliseconds(600));
                HWND hWnd = FindWindowA("Grand theft auto San Andreas", nullptr);
                if (hWnd) {
                    PostMessage(hWnd, WM_KEYDOWN, VK_DOWN, 0);
                    std::this_thread::sleep_for(std::chrono::milliseconds(50));
                    PostMessage(hWnd, WM_KEYUP, VK_DOWN, 0);
                    std::this_thread::sleep_for(std::chrono::milliseconds(200));
                    PostMessage(hWnd, WM_KEYDOWN, VK_RETURN, 0);
                    std::this_thread::sleep_for(std::chrono::milliseconds(50));
                    PostMessage(hWnd, WM_KEYUP, VK_RETURN, 0);

                    // Wait for Reason Dialog and paste reason
                    std::this_thread::sleep_for(std::chrono::milliseconds(600));
                    std::string cp1251Art = UTF8ToCP1251(art.c_str());
                    for (char c : cp1251Art) {
                        PostMessage(hWnd, WM_CHAR, (WPARAM)(unsigned char)c, 0);
                        std::this_thread::sleep_for(std::chrono::milliseconds(5));
                    }
                }
            } else {
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\x9B\xD0\xB8\xD1\x88\xD0\xB5\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\x92\xD0\xA3 \xD0\xBE\xD1\x82\xD0\xBC\xD0\xB5\xD0\xBD\xD0\xB5\xD0\xBD\xD0\xBE.").c_str());
                });
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
        }

        // --- Phase 2: Quote Citation ---
        if (doQuote && !quoteText.empty()) {
            g_FineSequenceState.store(2);
            g_FineSequenceGo.store(false);
            g_FineSequenceStop.store(false);

            std::string k_c = Gui::issueFineKeyStr;
            std::string k_x = Gui::cancelFineKeyStr;
            std::string msgUtf8 = "{D2A65E}[DURAN HELPER] {FFFFFF}\xD0\x9F\xD1\x80\xD0\xBE\xD1\x86\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD1\x82\xD1\x8C \xD1\x88\xD1\x82\xD1\x80\xD0\xB0\xD1\x84? {D2A65E}" + k_c + " {FFFFFF}- \xD0\xBD\xD0\xB0\xD1\x87\xD0\xB0\xD1\x82\xD1\x8C, {FF6B6B}" + k_x + " {FFFFFF}- \xD0\xBF\xD1\x80\xD0\xBE\xD0\xBF\xD1\x83\xD1\x81\xD1\x82\xD0\xB8\xD1\x82\xD1\x8C ({D2A65E}30 \xD1\x81\xD0\xB5\xD0\xBA{FFFFFF})";
            RunOnMainThread([m = UTF8ToCP1251(msgUtf8.c_str()), k_c, k_x]() {
                AddLocalSAMPMessage(m.c_str());
                if (Gui::notifyFineIssue) {
                    std::string nMsg = "\xD0\x9F\xD0\xA0\xD0\x9E\xD0\xA6\xD0\x98\xD0\xA2\xD0\x98\xD0\xA0\xD0\x9E\xD0\x92\xD0\x90\xD0\xA2\xD0\xAC \xD0\xA8\xD0\xA2\xD0\xA0\xD0\x90\xD0\xA4?";
                    Gui::AddNotification("CALCULATOR", nMsg, k_c, "\xD0\xBD\xD0\xB0\xD1\x87\xD0\xB0\xD1\x82\xD1\x8C", k_x, "\xD0\xBF\xD1\x80\xD0\xBE\xD0\xBF\xD1\x83\xD1\x81\xD1\x82\xD0\xB8\xD1\x82\xD1\x8C", 30.0f, true, IM_COL32(210, 166, 94, 255));
                }
            });

            auto start = std::chrono::steady_clock::now();
            bool confirmed = false;
            while (true) {
                if (g_FineSequenceCancel.load()) { g_FineSequenceState.store(0); return; }
                if (g_FineSequenceGo.load()) { confirmed = true; break; }
                if (g_FineSequenceStop.load()) { confirmed = false; break; }
                auto elapsed = std::chrono::steady_clock::now() - start;
                if (std::chrono::duration_cast<std::chrono::seconds>(elapsed).count() >= 30) break;
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }

            // Wait for key release
            RunOnMainThread([]() { Gui::ClearNotifications(); });
            while (((GetAsyncKeyState(Gui::issueFineKey) & 0x8000) != 0) || ((GetAsyncKeyState(Gui::cancelFineKey) & 0x8000) != 0)) {
                if (g_FineSequenceCancel.load()) { g_FineSequenceState.store(0); return; }
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }

            if (confirmed) {
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FFFFFF}\xD0\x9D\xD0\xB0\xD1\x87\xD0\xB8\xD0\xBD\xD0\xB0\xD1\x8E \xD1\x86\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5...").c_str());
                });
                std::this_thread::sleep_for(std::chrono::milliseconds(500));

                g_QuotingActive.store(true);
                g_CancelQuote.store(false);
                std::string finalQuote = quoteText;
                if (!doRevoke || !revoked) {
                    std::string revokePrefix = "\xD0\x90\xD0\xBD\xD0\xBD\xD1\x83\xD0\xBB\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\x92\xD0\xA3 \xD0\xBF\xD0\xBE:";
                    size_t rPos = finalQuote.find(revokePrefix);
                    if (rPos != std::string::npos) {
                        size_t endPos = finalQuote.find('\n', rPos);
                        if (endPos != std::string::npos) finalQuote.erase(rPos, endPos - rPos + 1);
                        else finalQuote.erase(rPos);
                    }
                }
                std::string cp1251 = UTF8ToCP1251(finalQuote.c_str());
                std::vector<std::string> lines;
                size_t pos = 0;
                while (pos < cp1251.length()) {
                    size_t nlPos = cp1251.find('\n', pos);
                    if (nlPos == std::string::npos) nlPos = cp1251.length();
                    std::string segment = cp1251.substr(pos, nlPos - pos);
                    if (!segment.empty() && segment.back() == '\r') segment.pop_back();
                    if (segment.empty()) { pos = nlPos + 1; continue; }
                    size_t s = 0; size_t maxLen = 83;
                    while (s < segment.length()) {
                        if (segment.length() - s <= maxLen) { lines.push_back(segment.substr(s)); break; }
                        size_t end = s + maxLen;
                        size_t spacePos = segment.rfind(' ', end);
                        if (spacePos != std::string::npos && spacePos > s) {
                            lines.push_back(segment.substr(s, spacePos - s)); s = spacePos + 1;
                        } else {
                            lines.push_back(segment.substr(s, maxLen)); s += maxLen;
                        }
                    }
                    pos = nlPos + 1;
                }
                for (const auto& line : lines) {
                    if (g_CancelQuote.load()) break;
                    std::string msg = line;
                    RunOnMainThread([msg]() { SendSAMPMessage(msg.c_str()); });
                    for (int i = 0; i < 10 && !g_CancelQuote.load(); i++) {
                        std::this_thread::sleep_for(std::chrono::milliseconds(100));
                    }
                }
                g_QuotingActive.store(false);
            } else {
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\xA6\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\xBF\xD1\x80\xD0\xBE\xD0\xBF\xD1\x83\xD1\x89\xD0\xB5\xD0\xBD\xD0\xBE.").c_str());
                });
            }
        }

        g_FineSequenceState.store(0);
    }).detach();
}


void ImitateKeyboardTyping(const std::string& str) {
    for (char c : str) {
        SHORT vkAndShift = VkKeyScan(c);
        if (vkAndShift == -1) continue;
        BYTE vk = vkAndShift & 0xFF;
        bool shift = (vkAndShift >> 8) & 1;
        
        if (shift) keybd_event(VK_SHIFT, MapVirtualKey(VK_SHIFT, 0), 0, 0);
        keybd_event(vk, MapVirtualKey(vk, 0), 0, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        keybd_event(vk, MapVirtualKey(vk, 0), KEYEVENTF_KEYUP, 0);
        if (shift) keybd_event(VK_SHIFT, MapVirtualKey(VK_SHIFT, 0), KEYEVENTF_KEYUP, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(15));
    }
}

void StartWantedSequence(const std::string& targetId, const std::string& articleMsg, int stars, bool doQuote, const std::string& quoteText) {
    if (g_WantedSequenceState.load() != 0 || g_FineSequenceState.load() != 0) return;
    
    Gui::Toggle(); // Hide overlay
    g_WantedSequenceCancel.store(false);
    g_WantedSequenceGo.store(false);
    g_WantedSequenceStop.store(false);
    
    std::thread([targetId, articleMsg, stars, doQuote, quoteText]() {
        g_WantedSequenceState.store(1);
        
        // Sequence: /su [id] [stars] -> Down -> Enter -> type articleMsg
        std::string suCmd = "/su " + targetId + " " + std::to_string(stars);
        RunOnMainThread([c = UTF8ToCP1251(suCmd.c_str())]() {
            SendSAMPMessage(c.c_str());
        });
        std::this_thread::sleep_for(std::chrono::milliseconds(150));
        
        // Press Down
        keybd_event(VK_DOWN, MapVirtualKey(VK_DOWN, 0), 0, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
        keybd_event(VK_DOWN, MapVirtualKey(VK_DOWN, 0), KEYEVENTF_KEYUP, 0);
        
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
        
        // Press Enter to open the text input dialog
        keybd_event(VK_RETURN, MapVirtualKey(VK_RETURN, 0), 0, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
        keybd_event(VK_RETURN, MapVirtualKey(VK_RETURN, 0), KEYEVENTF_KEYUP, 0);
        
        std::this_thread::sleep_for(std::chrono::milliseconds(600));
        
        // Type articles via PostMessage (reliable, same as fine sequence)
        std::string cp1251Article = UTF8ToCP1251(articleMsg.c_str());
        HWND hWnd = FindWindowA("Grand theft auto San Andreas", nullptr);
        if (hWnd) {
            for (char c : cp1251Article) {
                PostMessage(hWnd, WM_CHAR, (WPARAM)(unsigned char)c, 0);
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
        }
        
        // Step 3: Quote
        if (doQuote && !quoteText.empty()) {
            g_WantedSequenceState.store(2);
            g_WantedSequenceGo.store(false);
            g_WantedSequenceStop.store(false);
            
            std::string k_c = Gui::issueFineKeyStr;
            std::string k_x = Gui::cancelFineKeyStr;
            std::string qReqStr = "{D2A65E}[DURAN HELPER] {FFFFFF}Процитировать причину розыска? {D2A65E}" + k_c + " {FFFFFF}- начать, {FF6B6B}" + k_x + " {FFFFFF}- пропустить.";
            std::string qReq = UTF8ToCP1251(qReqStr.c_str());
            
            RunOnMainThread([qReq, k_c, k_x]() {
                AddLocalSAMPMessage(qReq.c_str());
                if (Gui::notifyFineIssue) {
                    std::string nMsg = "\xD0\x9F\xD0\xA0\xD0\x9E\xD0\xA6\xD0\x98\xD0\xA2\xD0\x98\xD0\xA0\xD0\x9E\xD0\x92\xD0\x90\xD0\xA2\xD0\xAC \xD0\xA0\xD0\x9E\xD0\x97\xD0\xAB\xD0\xA1\xD0\x9A?";
                    Gui::AddNotification("CALCULATOR", nMsg, k_c, "\xD0\xBD\xD0\xB0\xD1\x87\xD0\xB0\xD1\x82\xD1\x8C", k_x, "\xD0\xBF\xD1\x80\xD0\xBE\xD0\xBF\xD1\x83\xD1\x81\xD1\x82\xD0\xB8\xD1\x82\xD1\x8C", 30.0f, true, IM_COL32(210, 166, 94, 255));
                }
            });
            
            bool qConf = false;
            auto start = std::chrono::steady_clock::now();
            while (true) {
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
                if (g_WantedSequenceCancel.load()) { g_WantedSequenceState.store(0); return; }
                if (g_WantedSequenceGo.load()) { qConf = true; break; }
                if (g_WantedSequenceStop.load()) { qConf = false; break; }
                auto elapsed = std::chrono::steady_clock::now() - start;
                if (std::chrono::duration_cast<std::chrono::seconds>(elapsed).count() >= 30) break;
            }
            
            RunOnMainThread([]() { Gui::ClearNotifications(); });
            while (((GetAsyncKeyState(Gui::issueFineKey) & 0x8000) != 0) || ((GetAsyncKeyState(Gui::cancelFineKey) & 0x8000) != 0)) {
                if (g_WantedSequenceCancel.load()) { g_WantedSequenceState.store(0); return; }
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }
            
            if (qConf) {
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FFFFFF}Начинаю цитирование...").c_str());
                });
                std::this_thread::sleep_for(std::chrono::milliseconds(500));
                
                g_QuotingActive.store(true);
                g_CancelQuote.store(false);
                std::string cp1251 = UTF8ToCP1251(quoteText.c_str());
                
                std::vector<std::string> lines;
                size_t pos = 0;
                while (pos < cp1251.length()) {
                    size_t nlPos = cp1251.find('\n', pos);
                    if (nlPos == std::string::npos) nlPos = cp1251.length();
                    std::string segment = cp1251.substr(pos, nlPos - pos);
                    if (!segment.empty() && segment.back() == '\r') segment.pop_back();
                    if (segment.empty()) { pos = nlPos + 1; continue; }
                    
                    size_t s = 0; size_t maxLen = 83;
                    while (s < segment.length()) {
                        if (segment.length() - s <= maxLen) { lines.push_back(segment.substr(s)); break; }
                        size_t end = s + maxLen;
                        size_t spacePos = segment.rfind(' ', end);
                        if (spacePos != std::string::npos && spacePos > s) {
                            lines.push_back(segment.substr(s, spacePos - s)); s = spacePos + 1;
                        } else {
                            lines.push_back(segment.substr(s, maxLen)); s += maxLen;
                        }
                    }
                    pos = nlPos + 1;
                }
                for (const auto& line : lines) {
                    if (g_CancelQuote.load()) break;
                    std::string msg = line;
                    RunOnMainThread([msg]() { SendSAMPMessage(msg.c_str()); });
                    for (int i = 0; i < 15 && !g_CancelQuote.load(); i++) {
                        std::this_thread::sleep_for(std::chrono::milliseconds(100));
                    }
                }
                g_QuotingActive.store(false);
            } else {
                RunOnMainThread([]() {
                    AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}Цитирование пропущено.").c_str());
                });
            }
        }
        
        g_WantedSequenceState.store(0);
    }).detach();
}

// ===== Smart Quoting =====
void Gui::ExecuteLawQuote(const std::string& utf8text) {
    Gui::Toggle(); // Use Toggle to correctly hide overlay and restore game state
    g_QuotingActive.store(true);
    g_CancelQuote.store(false);
    
    std::thread([utf8text]() {
        std::string cleanUtf8 = utf8text;
        // Replace non-breaking space (U+00A0) with normal space
        size_t nPos = 0;
        while ((nPos = cleanUtf8.find("\xC2\xA0", nPos)) != std::string::npos) {
            cleanUtf8.replace(nPos, 2, " ");
        }
        // Replace zero-width space (U+200B) with nothing
        nPos = 0;
        while ((nPos = cleanUtf8.find("\xE2\x80\x8B", nPos)) != std::string::npos) {
            cleanUtf8.replace(nPos, 3, "");
        }
        
        Log("ExecuteLawQuote: Input UTF-8 text: " + utf8text);
        std::string cp1251 = UTF8ToCP1251(cleanUtf8.c_str());
        Log("ExecuteLawQuote: Converted CP1251 text: " + cp1251);
        std::vector<std::string> lines;
        
        // Split by \n first, then by maxLen
        size_t pos = 0;
        while (pos < cp1251.length()) {
            size_t nlPos = cp1251.find('\n', pos);
            if (nlPos == std::string::npos) nlPos = cp1251.length();
            
            std::string segment = cp1251.substr(pos, nlPos - pos);
            if (!segment.empty() && segment.back() == '\r') segment.pop_back();
            
            // Trim leading and trailing whitespace. If a line is ONLY spaces and '?' (from unconvertible chars like bullets or weird spaces), skip it.
            size_t first = segment.find_first_not_of(" \t\r\n?");
            if (first == std::string::npos) {
                pos = nlPos + 1;
                continue;
            }
            
            size_t last = segment.find_last_not_of(" \t\r\n");
            if (last != std::string::npos) {
                first = segment.find_first_not_of(" \t\r\n"); // real first non-whitespace
                segment = segment.substr(first, (last - first + 1));
            }
            
            if (segment.empty()) {
                pos = nlPos + 1;
                continue;
            }
            
            if (segment.back() == ')') {
                segment += "\xA0"; // Append non-breaking space (CP1251 byte)
            }
            
            size_t start = 0;
            size_t maxLen = 83; // SAMP chat optimal limit
            
            while (start < segment.length()) {
                if (segment.length() - start <= maxLen) {
                    lines.push_back(segment.substr(start));
                    break;
                }
                
                size_t end = start + maxLen;
                size_t spacePos = segment.rfind(' ', end);
                
                if (spacePos != std::string::npos && spacePos > start) {
                    lines.push_back(segment.substr(start, spacePos - start));
                    start = spacePos + 1;
                } else {
                    lines.push_back(segment.substr(start, maxLen));
                    start += maxLen;
                }
            }
            pos = nlPos + 1;
        }
        
        for (const auto& line : lines) {
            if (g_CancelQuote.load()) {
                break;
            }
            std::string msg = line;
            RunOnMainThread([msg]() {
                SendSAMPMessage(msg.c_str());
            });
            // Sleep for Gui::binderDelay ms but check cancellation dynamically
            if (Gui::binderDelay > 0) {
                int totalMs = Gui::binderDelay;
                int chunks = totalMs / 50;
                for (int i = 0; i < chunks && !g_CancelQuote.load(); i++) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(50));
                }
                int remainder = totalMs % 50;
                if (remainder > 0 && !g_CancelQuote.load()) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(remainder));
                }
            }
        }
        g_QuotingActive.store(false);
    }).detach();
}

// ===== Bind Execution (shared by WndProc hotkeys and overlay play button) =====
std::atomic<int> g_RunningBindsCount{0};

void ExecuteBindActions(const BindItem& bind) {
    std::thread([b = bind]() {
        g_RunningBindsCount++;
        Log("Executing Bind: " + b.name);
        g_CancelBind.store(false);  // reset cancel flag on each new bind



        for (const auto& step : b.steps) {
            if (g_CancelBind.load()) break;  // user pressed stop-bind key

            if (step.action == "CHAT" || step.action == "TEXT") {
                std::string processedText = step.value;
                // Parse *ВРЕМЯ* built-in variable (strict uppercase) FIRST, so it doesn't get overwritten by JSON placeholder
                std::string timeVars[] = { "*\xD0\x92\xD0\xA0\xD0\x95\xD0\x9C\xD0\xAF*" };
                for (const auto& tv : timeVars) {
                    size_t pos = 0;
                    while ((pos = processedText.find(tv, pos)) != std::string::npos) {
                        time_t now = time(0);
                        struct tm tstruct;
                        localtime_s(&tstruct, &now);
                        char buf[80];
                        strftime(buf, sizeof(buf), "%H:%M:%S", &tstruct);
                        processedText.replace(pos, tv.length(), buf);
                        pos += 8; // length of "HH:MM:SS"
                    }
                }

                // Then parse custom variables from JSON
                for (const auto& var : BinderManager::Get().Variables) {
                    if (var.first.empty()) continue;
                    size_t pos = 0;
                    while ((pos = processedText.find(var.first, pos)) != std::string::npos) {
                        processedText.replace(pos, var.first.length(), var.second);
                        pos += var.second.length();
                    }
                }

                std::string cpText = UTF8ToCP1251(processedText.c_str());
                int cursorOffset = 0;
                size_t idPos = cpText.find("*ID*");
                if (idPos == std::string::npos) idPos = cpText.find("*id*");
                
                if (idPos != std::string::npos) {
                    cpText.erase(idPos, 4);
                    if (!step.isEnter) {
                        cursorOffset = cpText.length() - idPos;
                    }
                }

                if (step.isEnter) {
                    // Enter mode: send directly via RakNet
                    RunOnMainThread([text = cpText]() {
                        SendSAMPMessage(text.c_str());
                    });
                } else {
                    // Wait mode: open chat with pre-filled text, user finishes manually
                    RunOnMainThread([text = cpText, cursorOffset]() {
                        OpenChatWithText(text.c_str(), cursorOffset);
                    });
                    
                    // Wait up to 1000ms for chat to open
                    int waitOpen = 0;
                    while (!g_ChatOpen.load() && waitOpen < 1000) {
                        if (g_CancelBind.load()) break;
                        std::this_thread::sleep_for(std::chrono::milliseconds(50));
                        waitOpen += 50;
                    }

                    // Wait for user to close chat (press Enter)
                    while (g_ChatOpen.load()) {
                        if (g_CancelBind.load()) break;
                        std::this_thread::sleep_for(std::chrono::milliseconds(50));
                    }
                    if (g_CancelBind.load()) break;
                }
            } else if (step.action == "WAIT") {
                try {
                    int manualDelay = std::stoi(step.value);
                    if (manualDelay > 0) {
                        int chunks = manualDelay / 100;
                        for (int i = 0; i < chunks; i++) {
                            if (g_CancelBind.load()) break;
                            std::this_thread::sleep_for(std::chrono::milliseconds(100));
                        }
                        int remainder = manualDelay % 100;
                        if (remainder > 0 && !g_CancelBind.load())
                            std::this_thread::sleep_for(std::chrono::milliseconds(remainder));
                    }
                } catch(...) {}
                continue;
            } else if (step.action == "BUTTON" || step.action == "PRESS") {
                int vk = BinderManager::StringToVK(step.value);
                
                if (vk > 0) {
                    INPUT inputs[2] = {};
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].ki.wVk = vk;
                    
                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].ki.wVk = vk;
                    inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
                    
                    SendInput(2, inputs, sizeof(INPUT));
                }
            }
            bool skipDefaultDelay = false;
            if (step.action == "WAIT") {
                skipDefaultDelay = true;
            } else {
                size_t idx = &step - &b.steps[0];
                if (idx + 1 < b.steps.size() && b.steps[idx + 1].action == "WAIT") {
                    skipDefaultDelay = true;
                }
            }

            // Sleep with cancel check: check every 100ms instead of blocking for full binderDelay
            if (!skipDefaultDelay && Gui::binderDelay > 0) {
                int chunks = Gui::binderDelay / 100;
                for (int i = 0; i < chunks; i++) {
                    if (g_CancelBind.load()) break;
                    std::this_thread::sleep_for(std::chrono::milliseconds(100));
                }
                int remainder = Gui::binderDelay % 100;
                if (remainder > 0 && !g_CancelBind.load())
                    std::this_thread::sleep_for(std::chrono::milliseconds(remainder));
            }
        }
        g_RunningBindsCount--;
    }).detach();
}

// ===== Auto-Replace Trigger Buffer =====
// Moved g_ChatBuffer and g_ChatOpen to the top

// ===== Radial Menu sensitivity save =====
float radialSavedSensX = -1.0f;
float radialSavedSensY = -1.0f;

// ===== WndProc Hook =====
// Forward declaration of the inner logic
static LRESULT WndProcInner(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam);

static LRESULT __stdcall WndProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    if (!g_Initialized || !oWndProc) {
        if (oWndProc) {
            return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
        }
        return DefWindowProcA(hWnd, uMsg, wParam, lParam);
    }

    return WndProcInner(hWnd, uMsg, wParam, lParam);
}

static LRESULT WndProcInner(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {

    // Auto-hide overlay if game loses focus (Alt-Tab, minimize)
    if (uMsg == WM_ACTIVATEAPP && wParam == FALSE) {
        if (Gui::show) Gui::Toggle();
        Gui::ClearNotifications();
        g_FineSequenceCancel.store(true);
        g_WantedSequenceCancel.store(true);
        if (Gui::radialMenuOpen || Gui::radialIdInputOpen) {
            Gui::radialMenuOpen = false;
            Gui::radialIdInputOpen = false;
            if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
            if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
        }
    }
    if (uMsg == WM_ACTIVATE && LOWORD(wParam) == WA_INACTIVE) {
        if (Gui::show) Gui::Toggle();
        Gui::ClearNotifications();
        g_FineSequenceCancel.store(true);
        g_WantedSequenceCancel.store(true);
        if (Gui::radialMenuOpen || Gui::radialIdInputOpen) {
            Gui::radialMenuOpen = false;
            Gui::radialIdInputOpen = false;
            if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
            if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
        }
    }

    // Block Alt key from activating the window menu (which freezes/mutes the game)
    if (uMsg == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_KEYMENU) {
        return 0;
    }

    if (uMsg == WM_APP + 778) {
        ProcessMainThreadTasks();
        return 0;
    }
    // Intercept Live Sync signal from C# Launcher
    if (uMsg == WM_APP + 777) {
        Log("LIVE SYNC: Received reload signal from launcher.");
        std::thread([]() {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            RunOnMainThread([]() {
                Gui::LoadSettings();        // ← sync ALL system hotkeys (toggle, binder hint, issue/cancel fine, stop bind)
                BinderManager::Get().ReloadBinds();
                Gui::LoadLaws();
                Gui::LoadFines();
                Gui::LoadWanted();
                Gui::LoadRadialConfig();
                Log("LIVE SYNC: complete. sectorCount=" + std::to_string(Gui::radialSectorCount));
            });
        }).detach();
        return 0;
    }
    // Force-hide cursor when our menu is not shown AND radial menu not open
    if (uMsg == WM_SETCURSOR && LOWORD(lParam) == HTCLIENT && !Gui::show && !Gui::radialMenuOpen && !Gui::radialIdInputOpen) {
        SetCursor(NULL);
        return TRUE;
    }

    if (uMsg == WM_KEYDOWN && wParam == VK_BACK) {
        g_CancelQuote.store(true);
    }
    bool isDown = false, isUp = false;
    int msgVk = 0;
    if (uMsg == WM_KEYDOWN || uMsg == WM_SYSKEYDOWN) { msgVk = wParam; isDown = true; }
    else if (uMsg == WM_KEYUP || uMsg == WM_SYSKEYUP) { msgVk = wParam; isUp = true; }
    else if (uMsg == WM_MBUTTONDOWN) { msgVk = VK_MBUTTON; isDown = true; }
    else if (uMsg == WM_MBUTTONUP) { msgVk = VK_MBUTTON; isUp = true; }
    else if (uMsg == WM_LBUTTONDOWN) { msgVk = VK_LBUTTON; isDown = true; }
    else if (uMsg == WM_LBUTTONUP) { msgVk = VK_LBUTTON; isUp = true; }
    else if (uMsg == WM_RBUTTONDOWN) { msgVk = VK_RBUTTON; isDown = true; }
    else if (uMsg == WM_RBUTTONUP) { msgVk = VK_RBUTTON; isUp = true; }
    else if (uMsg == WM_XBUTTONDOWN) { msgVk = (GET_XBUTTON_WPARAM(wParam) == XBUTTON1) ? VK_XBUTTON1 : VK_XBUTTON2; isDown = true; }
    else if (uMsg == WM_XBUTTONUP) { msgVk = (GET_XBUTTON_WPARAM(wParam) == XBUTTON1) ? VK_XBUTTON1 : VK_XBUTTON2; isUp = true; }

    // Block ALL mouse clicks from reaching game while radial is open (prevents punching)
    if (Gui::radialMenuOpen && !Gui::show) {
        if (uMsg == WM_LBUTTONDOWN || uMsg == WM_LBUTTONUP || uMsg == WM_LBUTTONDBLCLK ||
            uMsg == WM_RBUTTONDOWN || uMsg == WM_RBUTTONUP || uMsg == WM_RBUTTONDBLCLK ||
            uMsg == WM_MBUTTONDOWN || uMsg == WM_MBUTTONUP || uMsg == WM_MBUTTONDBLCLK) {
            // Zero GTA pad state immediately
            *(uint8_t*)0xB73418 = 0; *(uint8_t*)0xB73404 = 0; // lmb
            *(uint8_t*)0xB73419 = 0; *(uint8_t*)0xB73405 = 0; // rmb
            *(uint8_t*)0xB7341A = 0; *(uint8_t*)0xB73406 = 0; // mmb
        }
    }

    // Refine generic modifier keys to specific Left/Right variants for matching binds (e.g. LCTRL)
    if (msgVk == VK_CONTROL) msgVk = (lParam & (1 << 24)) ? VK_RCONTROL : VK_LCONTROL;
    if (msgVk == VK_SHIFT) msgVk = MapVirtualKey((lParam & 0x00ff0000) >> 16, MAPVK_VSC_TO_VK_EX);
    if (msgVk == VK_MENU) msgVk = (lParam & (1 << 24)) ? VK_RMENU : VK_LMENU;

    bool tAltDown = (msgVk == VK_MENU || msgVk == VK_LMENU || msgVk == VK_RMENU) ? Gui::toggleNeedsAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
    bool tCtrlDown = (msgVk == VK_CONTROL || msgVk == VK_LCONTROL || msgVk == VK_RCONTROL) ? Gui::toggleNeedsCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
    bool tShiftDown = (msgVk == VK_SHIFT || msgVk == VK_LSHIFT || msgVk == VK_RSHIFT) ? Gui::toggleNeedsShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);

    // Stop-Bind key: abort a running binder sequence
    if (isDown && !Gui::stopBindKeyStr.empty() && BinderManager::Get().StringToVK(Gui::stopBindKeyStr) == msgVk && !Gui::show) {
        extern std::atomic<bool> g_CancelBind;
        extern std::atomic<int> g_RunningBindsCount;
        
        bool isQuoting = g_QuotingActive.load();
        bool isBinding = g_RunningBindsCount.load() > 0;
        
        if (isQuoting || isBinding) {
            g_CancelQuote.store(true);  // stops smart quote
            g_CancelBind.store(true);   // stops binds
            
            if (isQuoting) {
                RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\xA6\xD0\xB8\xD1\x82\xD0\xB8\xD1\x80\xD0\xBE\xD0\xB2\xD0\xB0\xD0\xBD\xD0\xB8\xD0\xB5 \xD0\xBE\xD1\x81\xD1\x82\xD0\xB0\xD0\xBD\xD0\xBE\xD0\xB2\xD0\xBB\xD0\xB5\xD0\xBD\xD0\xBE.").c_str()); });
            } else if (isBinding) {
                RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4 \xD0\xBE\xD1\x81\xD1\x82\xD0\xB0\xD0\xBD\xD0\xBE\xD0\xB2\xD0\xBB\xD0\xB5\xD0\xBD.").c_str()); });
            }
        }
    }

    if (isDown && msgVk == Gui::toggleKey && tAltDown == Gui::toggleNeedsAlt && tCtrlDown == Gui::toggleNeedsCtrl && tShiftDown == Gui::toggleNeedsShift && Gui::scriptEnabled) {
        if (Gui::radialMenuOpen || Gui::radialIdInputOpen) return 0; // block while radial is open
        Gui::Toggle();
        return 0;
    }

    if (Gui::scriptToggleKey != 0) {
        bool scAlt = (msgVk == VK_MENU || msgVk == VK_LMENU || msgVk == VK_RMENU) ? Gui::scriptToggleNeedsAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
        bool scCtrl = (msgVk == VK_CONTROL || msgVk == VK_LCONTROL || msgVk == VK_RCONTROL) ? Gui::scriptToggleNeedsCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
        bool scShift = (msgVk == VK_SHIFT || msgVk == VK_LSHIFT || msgVk == VK_RSHIFT) ? Gui::scriptToggleNeedsShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
        if (isDown && msgVk == Gui::scriptToggleKey && scAlt == Gui::scriptToggleNeedsAlt && scCtrl == Gui::scriptToggleNeedsCtrl && scShift == Gui::scriptToggleNeedsShift) {
            Gui::scriptEnabled = !Gui::scriptEnabled;
            if (!Gui::scriptEnabled) {
                RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB5\xD1\x80 \xD0\xBE\xD1\x82\xD0\xBA\xD0\xBB\xD1\x8E\xD1\x87\xD0\xB5\xD0\xBD").c_str()); });
                Gui::AddNotification("HELPER", "\xD0\xA5\xD0\x95\xD0\x9B\xD0\x9F\xD0\x95\xD0\xA0 \xD0\x9E\xD0\xA2\xD0\x9A\xD0\x9B\xD0\xAE\xD0\xA7\xD0\x95\xD0\x9D", "", "", "", "", 5.0f, false, IM_COL32(239, 68, 68, 255));
                Gui::show = false;
                Gui::radialMenuOpen = false;
                Gui::radialIdInputOpen = false;
            } else {
                RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {2EA043}\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB5\xD1\x80 \xD0\xB2\xD0\xBA\xD0\xBB\xD1\x8E\xD1\x87\xD0\xB5\xD0\xBD").c_str()); });
                Gui::AddNotification("HELPER", "\xD0\xA5\xD0\x95\xD0\x9B\xD0\x9F\xD0\x95\xD0\xA0 \xD0\x92\xD0\x9A\xD0\x9B\xD0\xAE\xD0\xA7\xD0\x95\xD0\x9D", "", "", "", "", 5.0f, false, IM_COL32(34, 197, 94, 255));
            }
            return 0;
        }
    }

    if ((isDown || isUp) && msgVk == VK_ESCAPE && Gui::show) {
        if (isUp) Gui::Toggle();
        return 0; // prevent game pause menu from opening
    }

    if (isDown && msgVk == Gui::binderHintKey && !Gui::radialMenuOpen && !Gui::radialIdInputOpen && Gui::scriptEnabled && Gui::binderEnabled) {
        bool hAlt = (msgVk == VK_MENU || msgVk == VK_LMENU || msgVk == VK_RMENU) ? Gui::binderHintNeedsAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
        bool hCtrl = (msgVk == VK_CONTROL || msgVk == VK_LCONTROL || msgVk == VK_RCONTROL) ? Gui::binderHintNeedsCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
        bool hShift = (msgVk == VK_SHIFT || msgVk == VK_LSHIFT || msgVk == VK_RSHIFT) ? Gui::binderHintNeedsShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
        if (hAlt == Gui::binderHintNeedsAlt && hCtrl == Gui::binderHintNeedsCtrl && hShift == Gui::binderHintNeedsShift) {
            Gui::ToggleBinderHint();
            return 0;
        }
    }

    // ===== Radial Menu =====
    if (Gui::radialEnabled && Gui::radialSectorCount > 0 && !Gui::radialSectors.empty() && !Gui::show && !Gui::radialIdInputOpen) {
        int radVk = VK_MBUTTON;
        bool radAlt = false, radCtrl = false, radShift = false;
        for (auto& b : BinderManager::Get().Binds) {
            if (b.id == "Radial") {
                if (b.vkCode != 0) radVk = b.vkCode;
                radAlt = b.needsAlt; radCtrl = b.needsCtrl; radShift = b.needsShift;
                break;
            }
        }

        if (msgVk == radVk) {
            bool altDown = (msgVk == VK_MENU || msgVk == VK_LMENU || msgVk == VK_RMENU) ? radAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
            bool ctrlDown = (msgVk == VK_CONTROL || msgVk == VK_LCONTROL || msgVk == VK_RCONTROL) ? radCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
            bool shiftDown = (msgVk == VK_SHIFT || msgVk == VK_LSHIFT || msgVk == VK_RSHIFT) ? radShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
            
            if (isDown && !Gui::radialMenuOpen && altDown == radAlt && ctrlDown == radCtrl && shiftDown == radShift) {
                // Move OS cursor to screen center BEFORE enabling radial (hook blocks SetCursorPos when radial is open)
                RECT rc;
                if (GetClientRect(hWnd, &rc)) {
                    POINT center = { (rc.right - rc.left) / 2, (rc.bottom - rc.top) / 2 };
                    ClientToScreen(hWnd, &center);
                    SetCursorPos(center.x, center.y);
                }
                Gui::radialMenuOpen = true;
                Gui::radialJustOpened = true;
                Gui::radialHoveredSector = -1;
                Gui::radialHoveredGroup = -1;
                Gui::radialSelectedGroup = -1;
                radialSavedSensX = *(float*)0xB6EC1C;
                radialSavedSensY = *(float*)0xB6EC18;
                *(float*)0xB6EC1C = 0.0f;
                *(float*)0xB6EC18 = 0.0f;
                return 0;
            } else if ((isUp && !Gui::radialActivationToggleMode && Gui::radialMenuOpen) || 
                       (isDown && Gui::radialActivationToggleMode && Gui::radialMenuOpen && altDown == radAlt && ctrlDown == radCtrl && shiftDown == radShift)) {
                Gui::radialMenuOpen = false;
                
                if (Gui::radialMode == "Grouped" && Gui::radialSelectedGroup != -1) {
                    int sel = Gui::radialHoveredSector;
                    if (sel >= 0 && sel < (int)Gui::radialGroups[Gui::radialSelectedGroup].sectors.size() && !Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].bindId.empty()) {
                        if (Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].requiresId) {
                            Gui::radialIdInputOpen = true;
                            Gui::radialIdTargetSector = sel;
                            Gui::radialIdFocusRequest = true;
                            memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
                        } else {
                            std::string bindId = Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].bindId;
                            if (bindId == "open_fines") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 1;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else if (bindId == "open_wanted") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 4;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else {
                                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                                for (auto& b : BinderManager::Get().Binds) {
                                    if (b.id == bindId) { ExecuteBindActions(b); break; }
                                }
                            }
                        }
                    } else {
                        if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                        if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                    }
                } else if (Gui::radialMode != "Grouped") {
                    int sel = Gui::radialHoveredSector;
                    if (sel >= 0 && sel < (int)Gui::radialSectors.size() && !Gui::radialSectors[sel].bindId.empty()) {
                        if (Gui::radialSectors[sel].requiresId) {
                            Gui::radialIdInputOpen = true;
                            Gui::radialIdTargetSector = sel;
                            Gui::radialIdFocusRequest = true;
                            memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
                        } else {
                            std::string bindId = Gui::radialSectors[sel].bindId;
                            if (bindId == "open_fines") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 1;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else if (bindId == "open_wanted") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 4;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else {
                                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                                for (auto& b : BinderManager::Get().Binds) {
                                    if (b.id == bindId) { ExecuteBindActions(b); break; }
                                }
                            }
                        }
                    } else {
                        if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                        if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                    }
                } else {
                    if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                    if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                }
                return 0;
            }
        }
    }
    // Intercept only radial ID editing keys. Other keys should still reach the game.
    if (Gui::radialIdInputOpen) {
        if (uMsg == WM_KEYDOWN) {
            if (wParam == VK_ESCAPE) {
                Gui::radialIdInputOpen = false;
                extern float radialSavedSensX, radialSavedSensY;
                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
            } else if (wParam == VK_RETURN) {
                if (strlen(Gui::radialIdBuffer) > 0 && Gui::radialIdTargetSector >= 0) {
                    std::string bindId = "";
                    if (Gui::radialMode == "Grouped" && Gui::radialSelectedGroup != -1) {
                        if ((size_t)Gui::radialIdTargetSector < Gui::radialGroups[Gui::radialSelectedGroup].sectors.size()) {
                            bindId = Gui::radialGroups[Gui::radialSelectedGroup].sectors[Gui::radialIdTargetSector].bindId;
                        }
                    } else if (Gui::radialIdTargetSector < (int)Gui::radialSectors.size()) {
                        bindId = Gui::radialSectors[Gui::radialIdTargetSector].bindId;
                    }

                    if (!bindId.empty()) {
                        std::string targetId = Gui::radialIdBuffer;
                    for (auto& b : BinderManager::Get().Binds) {
                        if (b.id == bindId) {
                            BindItem copy = b;
                            bool idReplaced = false;
                            for (auto& step : copy.steps) {
                                if (step.action == "CHAT" || step.action == "TEXT") {
                                    size_t idPos = step.value.find("*ID*");
                                    if (idPos == std::string::npos) idPos = step.value.find("*id*");
                                    if (idPos != std::string::npos) {
                                        while (idPos != std::string::npos) {
                                            step.value.replace(idPos, 4, targetId);
                                            idPos = step.value.find("*ID*");
                                            if (idPos == std::string::npos) idPos = step.value.find("*id*");
                                        }
                                        idReplaced = true;
                                    }
                                }
                            }
                            if (!idReplaced) {
                                for (auto& step : copy.steps) {
                                    if (!step.isEnter && (step.action == "CHAT" || step.action == "TEXT")) {
                                        if (!step.value.empty() && step.value.back() != ' ') step.value += " ";
                                        step.value += targetId;
                                        step.isEnter = true;
                                        break;
                                    }
                                }
                            }
                            ExecuteBindActions(copy);
                            break;
                        }
                    }
                    }
                }
                Gui::radialIdInputOpen = false;
                extern float radialSavedSensX, radialSavedSensY;
                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
                return 0;
            } else if (wParam == VK_BACK) {
                size_t len = strlen(Gui::radialIdBuffer);
                if (len > 0) Gui::radialIdBuffer[len - 1] = '\0';
                return 0;
            } else if ((wParam >= '0' && wParam <= '9') || (wParam >= VK_NUMPAD0 && wParam <= VK_NUMPAD9)) {
                char digit = (wParam >= VK_NUMPAD0 && wParam <= VK_NUMPAD9)
                    ? (char)('0' + (wParam - VK_NUMPAD0))
                    : (char)wParam;
                size_t len = strlen(Gui::radialIdBuffer);
                if (len < 3) {
                    Gui::radialIdBuffer[len] = digit;
                    Gui::radialIdBuffer[len + 1] = '\0';
                }
                return 0;
            }
        } else if (uMsg == WM_CHAR && wParam >= '0' && wParam <= '9') {
            // Characters for digits are already handled in WM_KEYDOWN.
            return 0;
        } else if (uMsg == WM_MOUSEWHEEL) {
            // Prevent wheel from affecting game while ID entry is open.
            return 0;
        }
    }

    // Auto-trigger: track chat state and typed characters
    if (!Gui::show && !Gui::radialIdInputOpen) {
        // T opens chat — skip the 't' char that follows
        if (uMsg == WM_KEYDOWN && wParam == 'T' && !g_ChatOpen.load()) {
            g_ChatOpen.store(true);
            g_ChatBuffer.clear();
            g_SkipNextChar = true; // Don't add 't' to buffer
        }
        // F6 opens chat — no character to skip
        if (uMsg == WM_KEYDOWN && wParam == VK_F6 && !g_ChatOpen.load()) {
            g_ChatOpen.store(true);
            g_ChatBuffer.clear();
        }
        // Enter/Escape close chat
        if (uMsg == WM_KEYDOWN && (wParam == VK_RETURN || wParam == VK_ESCAPE) && g_ChatOpen.load()) {
            g_ChatOpen.store(false);
            g_ChatBuffer.clear();
        }

        // Track typed characters using WM_KEYDOWN and keyboard layout translation
        // This is much more reliable than WM_CHAR which can lose Cyrillic decoding
        if (uMsg == WM_KEYDOWN && g_ChatOpen.load() && wParam != VK_BACK && wParam != VK_RETURN && wParam != VK_ESCAPE && wParam != VK_SHIFT && wParam != VK_CONTROL && wParam != VK_MENU) {
            
            if (g_SkipNextChar && (wParam == 'T' || wParam == 't')) {
                g_SkipNextChar = false;
            } else {
                BYTE ks[256];
                if (GetKeyboardState(ks)) {
                    // Get current keyboard layout for the active thread
                    HKL hkl = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), NULL));
                    
                    wchar_t wc[5] = {0};
                    // Translate virtual key to Unicode character based on current layout
                    if (ToUnicodeEx(wParam, MapVirtualKeyEx(wParam, MAPVK_VK_TO_VSC, hkl), ks, wc, 4, 0, hkl) > 0) {
                        
                        // Convert that Unicode character to CP1251
                        char mb[5] = {0};
                        int mbLen = WideCharToMultiByte(1251, 0, wc, 1, mb, sizeof(mb), NULL, NULL);
                        
                        if (mbLen > 0 && (unsigned char)mb[0] >= 32) {
                            for (int ci = 0; ci < mbLen; ci++) g_ChatBuffer += mb[ci];
                            if (g_ChatBuffer.length() > 64) g_ChatBuffer = g_ChatBuffer.substr(g_ChatBuffer.length() - 64);
                            
                            

                            // Check auto-triggers
                            if (Gui::scriptEnabled && Gui::binderEnabled) {
                                for (auto& b : BinderManager::Get().Binds) {
                                    if (!b.isAuto || !b.active || b.autoTrigger.empty()) continue;

                                    std::string trigger = Utf8ToAnsi(b.autoTrigger);
                                if (trigger.empty()) continue;

                                if (g_ChatBuffer.length() >= trigger.length() && 
                                    g_ChatBuffer.substr(g_ChatBuffer.length() - trigger.length()) == trigger) {

                                    Log("AUTO: MATCH '" + trigger + "' -> bind: " + b.name);

                                    for (size_t i = 0; i < trigger.length(); i++) {
                                        PostMessage(hWnd, WM_KEYDOWN, VK_BACK, 0);
                                        PostMessage(hWnd, WM_KEYUP, VK_BACK, 0);
                                    }
                                    PostMessage(hWnd, WM_KEYDOWN, VK_ESCAPE, 0);
                                    PostMessage(hWnd, WM_KEYUP, VK_ESCAPE, 0);

                                    g_ChatOpen.store(false);
                                    g_ChatBuffer.clear();
                                    ExecuteBindActions(b);
                                    return 0;
                                }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Handle backspace
        if (uMsg == WM_KEYDOWN && wParam == VK_BACK && g_ChatOpen.load() && !g_ChatBuffer.empty()) {
            g_ChatBuffer.pop_back();
        }
    }

    // Sequence actions via hotkey
    if (isDown && !Gui::show && !Gui::radialMenuOpen && !Gui::radialIdInputOpen && Gui::scriptEnabled && Gui::binderEnabled) {
        if (g_FineSequenceState.load() != 0) {
            if (msgVk == Gui::issueFineKey && tAltDown == Gui::issueFineNeedsAlt && tCtrlDown == Gui::issueFineNeedsCtrl && tShiftDown == Gui::issueFineNeedsShift) {
                g_FineSequenceGo.store(true);
                return 0; // block from game
            }
            if (msgVk == Gui::cancelFineKey && tAltDown == Gui::cancelFineNeedsAlt && tCtrlDown == Gui::cancelFineNeedsCtrl && tShiftDown == Gui::cancelFineNeedsShift) {
                g_FineSequenceStop.store(true);
                return 0; // block from game
            }
        }
    }

    if (isDown && !Gui::show && !Gui::radialMenuOpen && !Gui::radialIdInputOpen && Gui::scriptEnabled && Gui::binderEnabled) {
        if (g_WantedSequenceState.load() == 2) {
            if (msgVk == Gui::issueFineKey && tAltDown == Gui::issueFineNeedsAlt && tCtrlDown == Gui::issueFineNeedsCtrl && tShiftDown == Gui::issueFineNeedsShift) {
                g_WantedSequenceGo.store(true);
                return 0; // block from game
            }
            if (msgVk == Gui::cancelFineKey && tAltDown == Gui::cancelFineNeedsAlt && tCtrlDown == Gui::cancelFineNeedsCtrl && tShiftDown == Gui::cancelFineNeedsShift) {
                g_WantedSequenceStop.store(true);
                return 0; // block from game
            }
        }
    }

    // Binds Engine (hotkey binds — skip auto binds)
    if (isDown && !Gui::show && !Gui::radialMenuOpen && !Gui::radialIdInputOpen && Gui::scriptEnabled && Gui::binderEnabled) {
        WPARAM preciseKey = msgVk;
        if (preciseKey == VK_CONTROL) preciseKey = (lParam & (1 << 24)) ? VK_RCONTROL : VK_LCONTROL;
        if (preciseKey == VK_MENU)    preciseKey = (lParam & (1 << 24)) ? VK_RMENU : VK_LMENU;
        if (preciseKey == VK_SHIFT)   preciseKey = MapVirtualKey((lParam & 0x00FF0000) >> 16, MAPVK_VSC_TO_VK_EX);

        const BindItem* matchedBind = nullptr;
        for (auto& b : BinderManager::Get().Binds) {
            // Also check standard generic modifiers just in case it was mapped generically
            if (!b.isAuto && (b.Matches(preciseKey) || b.Matches(wParam))) {
                matchedBind = &b;
                break;
            }
        }

        if (matchedBind) {
            ExecuteBindActions(*matchedBind);
            return 0;
        }
    }

    // Binds Engine for mouse XButtons (XButton1/XButton2)
    if (uMsg == WM_XBUTTONDOWN && !Gui::show && Gui::scriptEnabled && Gui::binderEnabled) {
        int xbtn = GET_XBUTTON_WPARAM(wParam);
        int vk = (xbtn == XBUTTON1) ? VK_XBUTTON1 : VK_XBUTTON2;
        const BindItem* matchedBind = nullptr;
        for (auto& b : BinderManager::Get().Binds) {
            if (!b.isAuto && b.Matches(vk)) {
                matchedBind = &b;
                break;
            }
        }
        if (matchedBind) {
            ExecuteBindActions(*matchedBind);
            return 0;
        }
    }

    // Radial Menu open: forward ALL mouse events to ImGui, block game
    if (Gui::radialMenuOpen && !Gui::show) {
        // Click mode: LMB selects group or executes bind
        if (uMsg == WM_LBUTTONDOWN && Gui::radialActivationToggleMode) {
            if (Gui::radialMode == "Grouped") {
                if (Gui::radialSelectedGroup == -1 && Gui::radialHoveredGroup != -1) {
                    // Click on a group to open it
                    Gui::radialSelectedGroup = Gui::radialHoveredGroup;
                    Gui::radialHoveredSector = -1;
                    return 0;
                } else if (Gui::radialSelectedGroup != -1 && Gui::radialHoveredSector != -1) {
                    // Click on a bind sector to execute
                    ExecuteRadialMenuSelection();
                    return 0;
                } else if (Gui::radialSelectedGroup != -1 && Gui::radialHoveredSector == -1) {
                    // Click on a different group or empty area
                    if (Gui::radialHoveredGroup != -1 && Gui::radialHoveredGroup != Gui::radialSelectedGroup) {
                        Gui::radialSelectedGroup = Gui::radialHoveredGroup;
                        Gui::radialHoveredSector = -1;
                    }
                    return 0;
                }
            } else {
                // Standard mode: click to execute
                if (Gui::radialHoveredSector != -1) {
                    ExecuteRadialMenuSelection();
                    return 0;
                }
            }
            return 0; // Always block LMB from game
        }
        // Hold mode: LMB selects group in Grouped mode
        if (uMsg == WM_LBUTTONDOWN && !Gui::radialActivationToggleMode && Gui::radialMode == "Grouped" && Gui::radialSelectedGroup == -1 && Gui::radialHoveredGroup != -1) {
            Gui::radialSelectedGroup = Gui::radialHoveredGroup;
            Gui::radialHoveredSector = -1;
            return 0;
        }
        // RMB: go back from selected group
        if (uMsg == WM_RBUTTONDOWN && Gui::radialMode == "Grouped" && Gui::radialSelectedGroup != -1) {
            Gui::radialSelectedGroup = -1;
            Gui::radialHoveredSector = -1;
            Gui::radialHoveredGroup = -1;
            return 0;
        }
        ImGui_ImplWin32_WndProcHandler(hWnd, uMsg, wParam, lParam);
        switch (uMsg) {
            case WM_MOUSEMOVE:
            case WM_LBUTTONDOWN: case WM_LBUTTONUP:
            case WM_RBUTTONDOWN: case WM_RBUTTONUP:
            case WM_MBUTTONDOWN: case WM_MBUTTONUP:
            case WM_INPUT:
                return 0;
        }
    }

    // Radial ID input: forward mouse to ImGui, but keep gameplay keyboard mostly intact.
    if (Gui::radialIdInputOpen && !Gui::show) {
        ImGui_ImplWin32_WndProcHandler(hWnd, uMsg, wParam, lParam);
        switch (uMsg) {
            case WM_MOUSEMOVE:
            case WM_LBUTTONDOWN: case WM_LBUTTONUP:
                return 0;
            case WM_MOUSEWHEEL:
                return 0;
        }
    }

    if (Gui::show) {
        ImGui_ImplWin32_WndProcHandler(hWnd, uMsg, wParam, lParam);

        bool wantTextInput = false;
        if (ImGui::GetCurrentContext() != nullptr) {
            wantTextInput = ImGui::GetIO().WantTextInput;
        }

        if (!Gui::blockKeyboardInput && !wantTextInput) {
            if (uMsg == WM_INPUT || uMsg == WM_KEYDOWN || uMsg == WM_KEYUP || 
                uMsg == WM_CHAR || uMsg == WM_SYSKEYDOWN || uMsg == WM_SYSKEYUP) {
                return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
            }
        }

        if (uMsg == WM_INPUT) {
            RAWINPUT ri;
            UINT dwSize = sizeof(RAWINPUT);
            if (GetRawInputData((HRAWINPUT)lParam, RID_INPUT, &ri, &dwSize, sizeof(RAWINPUTHEADER)) != (UINT)-1) {
                if (ri.header.dwType == RIM_TYPEKEYBOARD) {
                    USHORT vKey = ri.data.keyboard.VKey;
                    if (vKey == 'X' || vKey == VK_BACK) return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
                    if (!wantTextInput && (vKey == 'W' || vKey == 'A' || vKey == 'S' || vKey == 'D' || vKey == 'F' || vKey == VK_RETURN)) {
                        return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
                    }
                }
            }
        } else if (uMsg == WM_KEYDOWN || uMsg == WM_KEYUP) {
            if (wParam == 'X' || wParam == VK_BACK) return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
            if (!wantTextInput && (wParam == 'W' || wParam == 'A' || wParam == 'S' || wParam == 'D' || wParam == 'F' || wParam == VK_RETURN)) {
                return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
            }
        } else if (uMsg == WM_CHAR) {
            if (wParam == 'X' || wParam == 'x' || wParam == VK_BACK) return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
            if (!wantTextInput) {
                if (wParam == 'W' || wParam == 'w' || wParam == 'A' || wParam == 'a' || wParam == 'S' || wParam == 's' || wParam == 'D' || wParam == 'd' || wParam == 'F' || wParam == 'f' || wParam == VK_RETURN) {
                    return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
                }
            }
        }

        switch (uMsg) {
            case WM_LBUTTONDOWN: case WM_LBUTTONUP: case WM_LBUTTONDBLCLK:
            case WM_RBUTTONDOWN: case WM_RBUTTONUP: case WM_RBUTTONDBLCLK:
            case WM_MBUTTONDOWN: case WM_MBUTTONUP: case WM_MBUTTONDBLCLK:
            case WM_MOUSEWHEEL: case WM_MOUSEMOVE:
            case WM_KEYDOWN: case WM_KEYUP:
            case WM_CHAR: case WM_SYSKEYDOWN: case WM_SYSKEYUP:
            case WM_INPUT:
                return 0;
        }
        
        return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
    }

    return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
}

// ===== D3D9 Hook Functions =====
// These will be set up via kthook for D3D9 bodies instead of VTable rewrites!
typedef HRESULT(__stdcall* EndSceneFn)(IDirect3DDevice9*);
typedef HRESULT(__stdcall* ResetFn)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);

static kthook::kthook_simple<EndSceneFn> hookEndScene;
static kthook::kthook_simple<ResetFn> hookReset;

static HRESULT Hooked_EndScene(const kthook::kthook_simple<EndSceneFn>& hook, IDirect3DDevice9* pDevice) {
    static IDirect3DDevice9* currentDevice = nullptr;
    static bool firstEndScene = true;

    if (firstEndScene) {
        Log("Hooked_EndScene: First call executed on device " + std::to_string((uintptr_t)pDevice));
        firstEndScene = false;
    }
    
    // Handle Alt-Tab Device Recreation explicitly
    if (currentDevice != pDevice) {
        if (currentDevice != nullptr && g_Initialized) {
            Log("Hooked_EndScene: Device changed. Invalidating ImGui.");
            ImGui_ImplDX9_InvalidateDeviceObjects();
            ImGui_ImplDX9_Shutdown();
            ImGui_ImplWin32_Shutdown();
            ImGui::DestroyContext();
            g_Initialized = false;
        }
        currentDevice = pDevice;
    }

    if (!g_Initialized) {
        Log("Hooked_EndScene: Initializing ImGui...");
        BinderManager::Get().ReloadBinds();
        D3DDEVICE_CREATION_PARAMETERS cp;
        if (SUCCEEDED(pDevice->GetCreationParameters(&cp))) {
            g_hWnd = cp.hFocusWindow;
            Log("Hooked_EndScene: Found hFocusWindow: " + std::to_string((uintptr_t)g_hWnd));
            if (g_hWnd && !g_WndProcHooked) {
                // Hook WndProc ONCE — never re-hook to avoid recursion
                oWndProc = (WNDPROC)SetWindowLongPtrA(g_hWnd, GWLP_WNDPROC, (LONG_PTR)WndProc);
                g_WndProcHooked = true;
                Log("Hooked_EndScene: WndProc successfully hooked.");
            }
        }
        Gui::Init(pDevice);
        g_Initialized = true;
        Log("Hooked_EndScene: ImGui initialization complete.");
    }

    if (Gui::show || Gui::radialMenuOpen || Gui::radialIdInputOpen) {
        *(float*)0xB6EC1C = 0.0f;
        *(float*)0xB6EC18 = 0.0f;
    }

    Gui::Render();
    return hook.get_trampoline()(pDevice);
}

static HRESULT Hooked_Reset(const kthook::kthook_simple<ResetFn>& hook, IDirect3DDevice9* pDevice, D3DPRESENT_PARAMETERS* pParams) {
    Log("Hooked_Reset: Display reset requested.");
    if (g_Initialized) {
        ImGui_ImplDX9_InvalidateDeviceObjects();
    }

    HRESULT hr = hook.get_trampoline()(pDevice, pParams);
    if (SUCCEEDED(hr)) {
        Log("Hooked_Reset: Reset successful.");
        if (g_Initialized) {
            ImGui_ImplDX9_CreateDeviceObjects();
        }
    } else {
        Log("Hooked_Reset: Reset failed.");
    }
    return hr;
}

static IDirect3DDevice9* GetGameDevice() {
    IDirect3DDevice9** ppDevice = (IDirect3DDevice9**)0xC97C28;
    if (ppDevice && *ppDevice) return *ppDevice;
    return nullptr;
}

static void MainThread() {
    while (!GetModuleHandleA("samp.dll")) Sleep(100);
    Log("MainThread: samp.dll found. Setting up hooks...");


    // We rely on the DispatchMessage hook below for the ultra-early welcome message.

    Sleep(5000);

    // ===== Hook RakClient::RPC via vtable to intercept /go and /stop =====
    {
        HMODULE hSampRpc = GetModuleHandleA("samp.dll");
        if (hSampRpc) {
            DWORD sBase = (DWORD)hSampRpc;
            DWORD pNetGamePtr = *(DWORD*)(sBase + 0x26E8DC);
            if (pNetGamePtr) {
                RakClientInterface** ppRC = (RakClientInterface**)(pNetGamePtr + 0x2C);
                if (ppRC && *ppRC) {
                    DWORD* vtable = *(DWORD**)*ppRC;
                    DWORD oldProt;
                    VirtualProtect(&vtable[25], 4, PAGE_READWRITE, &oldProt);
                    g_OrigRPC = (OrigRPC_t)vtable[25];
                    vtable[25] = (DWORD)&HookedRPC;
                    VirtualProtect(&vtable[25], 4, oldProt, &oldProt);
                    Log("MainThread: RPC vtable hook installed at index 25.");
                }
            }
        }
    }

    // ===== Hook SetCursorPos with kthook =====
    HMODULE hUser32 = GetModuleHandleA("user32.dll");
    if (hUser32) {
        oWndProc = (WNDPROC)GetProcAddress(hUser32, "DefWindowProcA");
        FARPROC setCursorPosAddr = GetProcAddress(hUser32, "SetCursorPos");
        if (setCursorPosAddr) {
            hookSetCursorPos.set_dest((SetCursorPos_t)setCursorPosAddr);
            hookSetCursorPos.before += [](const auto& hook, int& X, int& Y) -> std::optional<BOOL> {
                if (Gui::show || Gui::radialMenuOpen || Gui::radialIdInputOpen) {
                    return TRUE; // Block — return TRUE without calling original
                }
                return std::nullopt; // Call original
            };
            hookSetCursorPos.install();
        }
    }

    // ===== Hook CPad::UpdatePads with kthook to prevent control conflicts =====
    hookCPadUpdate.set_cb([](const auto& hook) {
        hook.get_trampoline()(); // Let GTA SA read the hardware controller mappings
        
        if (Gui::show || Gui::radialMenuOpen || Gui::radialIdInputOpen) {
            bool wantTextInput = false;
            if (ImGui::GetCurrentContext() != nullptr) {
                wantTextInput = ImGui::GetIO().WantTextInput;
            }
            
            if ((Gui::show && Gui::blockKeyboardInput) || wantTextInput) {
                // Hard-block everything when typing or explicitly blocked
                memset((void*)0xB73458, 0, 96);
            } else {
                // Allow WASD and F, but block mouse (camera and shooting) so clicks on overlay don't trigger game actions
                struct CControllerState {
                    signed short LeftStickX; signed short LeftStickY;
                    signed short RightStickX; signed short RightStickY;
                    signed short LeftShoulder1; signed short LeftShoulder2;
                    signed short RightShoulder1; signed short RightShoulder2;
                    signed short DPadUp; signed short DPadDown;
                    signed short DPadLeft; signed short DPadRight;
                    signed short Start; signed short Select;
                    signed short ButtonSquare; signed short ButtonTriangle;
                    signed short ButtonCross; signed short ButtonCircle;
                    signed short ShockButtonL; signed short ShockButtonR;
                    signed short m_bChatIndicated; signed short m_bPedWalk;
                    signed short m_bVehicleMouseLook; signed short m_bRadioTrackSkip;
                };
                for (int i = 0; i < 2; i++) {
                    CControllerState* state = (CControllerState*)(0xB73458 + (i * 48));
                    state->RightStickX = 0;
                    state->RightStickY = 0;
                    state->ButtonCircle = 0; // Fire (LMB)
                    state->LeftShoulder1 = 0; // Aim (RMB)
                    state->LeftShoulder2 = 0;
                    state->RightShoulder1 = 0;
                    state->RightShoulder2 = 0;
                }
            }
        }
    });
    hookCPadUpdate.install();

    // ===== Hook GetAsyncKeyState and GetKeyboardState to block SA-MP =====
    HMODULE hUser32_Hooks = GetModuleHandleA("user32.dll");
    if (hUser32_Hooks) {
        FARPROC getAsyncAddr = GetProcAddress(hUser32_Hooks, "GetAsyncKeyState");
        if (getAsyncAddr) {
            hookGetAsyncKeyState.set_dest((GetAsyncKeyState_t)getAsyncAddr);
            hookGetAsyncKeyState.before += [](const auto& hook, int& vKey) -> std::optional<SHORT> {
                bool wantTextInput = false;
                if (ImGui::GetCurrentContext() != nullptr) wantTextInput = ImGui::GetIO().WantTextInput;
                if (Gui::show && (Gui::blockKeyboardInput || wantTextInput) && (vKey == 'T' || vKey == 't' || vKey == VK_F6 || vKey == VK_F8 || vKey == VK_ESCAPE)) {
                    return 0; // Return unpressed for specific keys
                }
                return std::nullopt;
            };
            hookGetAsyncKeyState.install();
        }
        
        FARPROC getKeyboardStateAddr = GetProcAddress(hUser32_Hooks, "GetKeyboardState");
        if (getKeyboardStateAddr) {
            hookGetKeyboardState.set_dest((GetKeyboardState_t)getKeyboardStateAddr);
            hookGetKeyboardState.after += [](const auto& hook, BOOL& ret, PBYTE lpKeyState) {
                bool wantTextInput = false;
                if (ImGui::GetCurrentContext() != nullptr) wantTextInput = ImGui::GetIO().WantTextInput;
                if (Gui::show && (Gui::blockKeyboardInput || wantTextInput) && ret && lpKeyState) {
                    lpKeyState['T'] = 0;
                    lpKeyState['t'] = 0;
                    lpKeyState[VK_F8] = 0;
                    lpKeyState[VK_ESCAPE] = 0;
                }
            };
            hookGetKeyboardState.install();
        }

        // ===== Intercept DispatchMessage to bypass SA-MP's WndProc hook entirely =====
        auto hookDispatchBefore = [](const auto& hook, const MSG*& msg) -> std::optional<LRESULT> {
            if (!msg) return std::nullopt;
            
            if (!g_chatWelcomeSent) {
                HMODULE hSampLoc = GetModuleHandleA("samp.dll");
                if (hSampLoc) {
                    DWORD* pChatPtr = (DWORD*)((DWORD)hSampLoc + 0x26E8C8);
                    if (pChatPtr && *pChatPtr) {
                        if (g_chatReadyTime == 0) g_chatReadyTime = GetTickCount64();
                        if (GetTickCount64() - g_chatReadyTime > 4000) { 
                            g_chatWelcomeSent = true;
                            // Build welcome message with version and toggle key
                            std::string ver = Gui::versionStr.empty() ? "" : (" V" + Gui::versionStr);
                            std::string msg1 = "{D2A65E}[DURAN HELPER] {FFFFFF}\xD0\x9F\xD0\xBB\xD0\xB0\xD0\xB3\xD0\xB8\xD0\xBD \xD1\x83\xD1\x81\xD0\xBF\xD0\xB5\xD1\x88\xD0\xBD\xD0\xBE \xD0\xB7\xD0\xB0\xD0\xB3\xD1\x80\xD1\x83\xD0\xB6\xD0\xB5\xD0\xBD! \xD0\x92\xD0\xB5\xD1\x80\xD1\x81\xD0\xB8\xD1\x8F" + ver;
                            std::string line1 = UTF8ToCP1251(msg1.c_str());
                            std::string line2 = UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FFFFFF}\xD0\x9D\xD0\xB0\xD1\x88\xD0\xB0 \xD0\xB3\xD1\x80\xD1\x83\xD0\xBF\xD0\xBF\xD0\xB0 VK: {1F6FEB}vk.com/duranhelper");
                            RunOnMainThread([l1=line1, l2=line2]() {
                                AddLocalSAMPMessage(l1.c_str());
                                AddLocalSAMPMessage(l2.c_str());
                            });
                        }
                    }
                }
            }

            bool tAltDown = (msg->wParam == VK_MENU || msg->wParam == VK_LMENU || msg->wParam == VK_RMENU) ? Gui::toggleNeedsAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
            bool tCtrlDown = (msg->wParam == VK_CONTROL || msg->wParam == VK_LCONTROL || msg->wParam == VK_RCONTROL) ? Gui::toggleNeedsCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
            bool tShiftDown = (msg->wParam == VK_SHIFT || msg->wParam == VK_LSHIFT || msg->wParam == VK_RSHIFT) ? Gui::toggleNeedsShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
            if ((msg->message == WM_KEYDOWN || msg->message == WM_SYSKEYDOWN) && msg->wParam == Gui::toggleKey && tAltDown == Gui::toggleNeedsAlt && tCtrlDown == Gui::toggleNeedsCtrl && tShiftDown == Gui::toggleNeedsShift && Gui::scriptEnabled) {
                if (!Gui::radialMenuOpen && !Gui::radialIdInputOpen) {
                    Gui::Toggle();
                }
                MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                return 0;
            }

            if (Gui::scriptToggleKey != 0) {
                bool scAlt = (msg->wParam == VK_MENU || msg->wParam == VK_LMENU || msg->wParam == VK_RMENU) ? Gui::scriptToggleNeedsAlt : ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0);
                bool scCtrl = (msg->wParam == VK_CONTROL || msg->wParam == VK_LCONTROL || msg->wParam == VK_RCONTROL) ? Gui::scriptToggleNeedsCtrl : ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
                bool scShift = (msg->wParam == VK_SHIFT || msg->wParam == VK_LSHIFT || msg->wParam == VK_RSHIFT) ? Gui::scriptToggleNeedsShift : ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0);
                if ((msg->message == WM_KEYDOWN || msg->message == WM_SYSKEYDOWN) && msg->wParam == Gui::scriptToggleKey && scAlt == Gui::scriptToggleNeedsAlt && scCtrl == Gui::scriptToggleNeedsCtrl && scShift == Gui::scriptToggleNeedsShift) {
                    Gui::scriptEnabled = !Gui::scriptEnabled;
                    if (!Gui::scriptEnabled) {
                        RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {FF6B6B}\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB5\xD1\x80 \xD0\xBE\xD1\x82\xD0\xBA\xD0\xBB\xD1\x8E\xD1\x87\xD0\xB5\xD0\xBD").c_str()); });
                        Gui::AddNotification("HELPER", "\xD0\xA5\xD0\x95\xD0\x9B\xD0\x9F\xD0\x95\xD0\xA0 \xD0\x9E\xD0\xA2\xD0\x9A\xD0\x9B\xD0\xAE\xD0\xA7\xD0\x95\xD0\x9D", "", "", "", "", 5.0f, false, IM_COL32(239, 68, 68, 255));
                        Gui::show = false;
                        Gui::radialMenuOpen = false;
                        Gui::radialIdInputOpen = false;
                    } else {
                        RunOnMainThread([]() { AddLocalSAMPMessage(UTF8ToCP1251("{D2A65E}[DURAN HELPER] {2EA043}\xD0\x91\xD0\xB8\xD0\xBD\xD0\xB4\xD0\xB5\xD1\x80 \xD0\xB2\xD0\xBA\xD0\xBB\xD1\x8E\xD1\x87\xD0\xB5\xD0\xBD").c_str()); });
                        Gui::AddNotification("HELPER", "\xD0\xA5\xD0\x95\xD0\x9B\xD0\x9F\xD0\x95\xD0\xA0 \xD0\x92\xD0\x9A\xD0\x9B\xD0\xAE\xD0\xA7\xD0\x95\xD0\x9D", "", "", "", "", 5.0f, false, IM_COL32(34, 197, 94, 255));
                    }
                    MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                    return 0;
                }
            }

            if ((msg->message == WM_KEYDOWN || msg->message == WM_KEYUP) && msg->wParam == VK_ESCAPE && Gui::show) {
                if (msg->message == WM_KEYUP) Gui::Toggle();
                MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                return 0;
            }
            if (Gui::show) {
                if ((msg->message >= WM_KEYFIRST && msg->message <= WM_KEYLAST) || 
                    msg->message == WM_INPUT) {
                    bool wantText = false;
                    if (ImGui::GetCurrentContext() != nullptr) wantText = ImGui::GetIO().WantTextInput;

                    if (msg->message == WM_INPUT) {
                        RAWINPUT ri;
                        UINT dwSize = sizeof(RAWINPUT);
                        if (GetRawInputData((HRAWINPUT)msg->lParam, RID_INPUT, &ri, &dwSize, sizeof(RAWINPUTHEADER)) != (UINT)-1) {
                            if (ri.header.dwType == RIM_TYPEKEYBOARD) {
                                USHORT vKey = ri.data.keyboard.VKey;
                                if (vKey == 'X' || vKey == VK_BACK) return std::nullopt;
                                if (!wantText && (vKey == 'W' || vKey == 'A' || vKey == 'S' || vKey == 'D' || vKey == 'F' || vKey == VK_RETURN)) {
                                    return std::nullopt;
                                }
                            }
                        }
                    } else if (msg->message == WM_KEYDOWN || msg->message == WM_KEYUP) {
                        if (msg->wParam == 'X' || msg->wParam == VK_BACK) return std::nullopt;
                        if (!wantText && (msg->wParam == 'W' || msg->wParam == 'A' || msg->wParam == 'S' || msg->wParam == 'D' || msg->wParam == 'F' || msg->wParam == VK_RETURN)) {
                            return std::nullopt;
                        }
                    } else if (msg->message == WM_CHAR) {
                        if (msg->wParam == 'X' || msg->wParam == 'x' || msg->wParam == VK_BACK) return std::nullopt;
                        if (!wantText) {
                            WPARAM k = msg->wParam;
                            if (k == 'W' || k == 'w' || k == 'A' || k == 'a' || k == 'S' || k == 's' || k == 'D' || k == 'd' || k == 'F' || k == 'f' || k == VK_RETURN) {
                                return std::nullopt;
                            }
                        }
                    }
                    ImGui_ImplWin32_WndProcHandler(msg->hwnd, msg->message, msg->wParam, msg->lParam);
                    MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                    return 0; // Block SA-MP entirely
                }
            }
            // Block raw input from SA-MP while radial menu is open (prevents LMB punching via DirectInput)
            if (Gui::radialMenuOpen) {
                if (msg->message == WM_INPUT) {
                    // Consume raw input data so SA-MP/GTA can't read it
                    RAWINPUT ri;
                    UINT dwSize = sizeof(RAWINPUT);
                    GetRawInputData((HRAWINPUT)msg->lParam, RID_INPUT, &ri, &dwSize, sizeof(RAWINPUTHEADER));
                    MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                    return 0;
                }
                // Zero CMouseControllerState NewState.lmb and OldState.lmb
                *(uint8_t*)0xB73418 = 0; 
                *(uint8_t*)0xB73404 = 0; 
            }
            // Block raw input for radial ID input (but let keyboard through for number input!)
            if (Gui::radialIdInputOpen) {
                if (msg->message == WM_INPUT) {
                    RAWINPUT ri;
                    UINT dwSize = sizeof(RAWINPUT);
                    GetRawInputData((HRAWINPUT)msg->lParam, RID_INPUT, &ri, &dwSize, sizeof(RAWINPUTHEADER));
                    MSG* m = const_cast<MSG*>(msg); m->message = WM_NULL;
                    return 0;
                }
            }
            return std::nullopt;
        };

        FARPROC dma = GetProcAddress(hUser32_Hooks, "DispatchMessageA");
        if (dma) {
            hookDispatchMessageA.set_dest(dma);
            hookDispatchMessageA.before += hookDispatchBefore;
            hookDispatchMessageA.install();
        }
        
        FARPROC dmw = GetProcAddress(hUser32_Hooks, "DispatchMessageW");
        if (dmw) {
            hookDispatchMessageW.set_dest(dmw);
            hookDispatchMessageW.before += hookDispatchBefore;
            hookDispatchMessageW.install();
        }
    }

    // Chat hook was moved to immediately after GetModuleHandleA

    // ===== Hook D3D9 via kthook =====
    IDirect3DDevice9* pDevice = GetGameDevice();
    
    if (!pDevice) {
        Log("MainThread: GetGameDevice() returned nullptr. Creating a dummy device to get VTable...");
        IDirect3D9* pD3D = Direct3DCreate9(D3D_SDK_VERSION);
        if (!pD3D) { Log("MainThread: Direct3DCreate9 failed!"); return; }
        HWND hWnd = FindWindowA("Grand theft auto San Andreas", nullptr);
        if (!hWnd) hWnd = GetDesktopWindow();
        D3DPRESENT_PARAMETERS d3dpp = {};
        d3dpp.Windowed = TRUE; d3dpp.SwapEffect = D3DSWAPEFFECT_DISCARD;
        d3dpp.hDeviceWindow = hWnd; d3dpp.BackBufferFormat = D3DFMT_UNKNOWN;
        HRESULT hr = pD3D->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, d3dpp.hDeviceWindow,
            D3DCREATE_SOFTWARE_VERTEXPROCESSING, &d3dpp, &pDevice);
        if (FAILED(hr)) { Log("MainThread: CreateDevice on dummy failed."); pD3D->Release(); return; }
        
        void** vtable = *(void***)pDevice;
        Log("MainThread: Dummy VTable ready. Hooking EndScene (42) and Reset (16) via kthook...");
        hookEndScene.set_dest(vtable[42]);
        hookEndScene.set_cb(Hooked_EndScene);
        hookEndScene.install();
        
        hookReset.set_dest(vtable[16]);
        hookReset.set_cb(Hooked_Reset);
        hookReset.install();

        pDevice->Release(); pD3D->Release();
        Log("MainThread: Dummy D3D hooks installed via kthook.");
        return;
    }
    
    Log("MainThread: GetGameDevice() found existing device. Hooking VTable directly via kthook.");
    void** vtable = *(void***)pDevice;
    hookEndScene.set_dest(vtable[42]);
    hookEndScene.set_cb(Hooked_EndScene);
    hookEndScene.install();
    
    hookReset.set_dest(vtable[16]);
    hookReset.set_cb(Hooked_Reset);
    hookReset.install();
    Log("MainThread: Existing device D3D hooks installed via kthook.");
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
        CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)MainThread, NULL, 0, NULL);
        break;
    }
    return TRUE;
}

void ExecuteRadialMenuSelection() {
Gui::radialMenuOpen = false;
                
                if (Gui::radialMode == "Grouped" && Gui::radialSelectedGroup != -1) {
                    int sel = Gui::radialHoveredSector;
                    if (sel >= 0 && sel < (int)Gui::radialGroups[Gui::radialSelectedGroup].sectors.size() && !Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].bindId.empty()) {
                        if (Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].requiresId) {
                            Gui::radialIdInputOpen = true;
                            Gui::radialIdTargetSector = sel;
                            Gui::radialIdFocusRequest = true;
                            memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
                        } else {
                            std::string bindId = Gui::radialGroups[Gui::radialSelectedGroup].sectors[sel].bindId;
                            if (bindId == "open_fines") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 1;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else if (bindId == "open_wanted") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 4;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else {
                                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                                for (auto& b : BinderManager::Get().Binds) {
                                    if (b.id == bindId) { ExecuteBindActions(b); break; }
                                }
                            }
                        }
                    } else {
                        if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                        if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                    }
                } else if (Gui::radialMode != "Grouped") {
                    int sel = Gui::radialHoveredSector;
                    if (sel >= 0 && sel < (int)Gui::radialSectors.size() && !Gui::radialSectors[sel].bindId.empty()) {
                        if (Gui::radialSectors[sel].requiresId) {
                            Gui::radialIdInputOpen = true;
                            Gui::radialIdTargetSector = sel;
                            Gui::radialIdFocusRequest = true;
                            memset(Gui::radialIdBuffer, 0, sizeof(Gui::radialIdBuffer));
                        } else {
                            std::string bindId = Gui::radialSectors[sel].bindId;
                            if (bindId == "open_fines") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 1;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else if (bindId == "open_wanted") {
                                savedSensX = radialSavedSensX;
                                savedSensY = radialSavedSensY;
                                Gui::activeTab = 4;
                                Gui::showSettings = false;
                                Gui::show = true;
                            } else {
                                if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                                if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                                for (auto& b : BinderManager::Get().Binds) {
                                    if (b.id == bindId) { ExecuteBindActions(b); break; }
                                }
                            }
                        }
                    } else {
                        if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                        if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                    }
                } else {
                    if (radialSavedSensX >= 0.0f) *(float*)0xB6EC1C = radialSavedSensX;
                    if (radialSavedSensY >= 0.0f) *(float*)0xB6EC18 = radialSavedSensY;
                }
                }


