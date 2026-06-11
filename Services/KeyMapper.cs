using System;
using System.Collections.Generic;

namespace FSB_helper_C__.Services
{
    public static class KeyMapper
    {
        // WPF Key.ToString() → AHK hotkey name mapping (по документации AHK)
        public static readonly Dictionary<string, string> WpfToAhkKey = new(StringComparer.OrdinalIgnoreCase) {
            // === OEM / Punctuation keys ===
            {"Oem5", "\\"}, {"OemPipe", "\\"}, 
            {"Oem3", "``"}, {"OemTilde", "``"},              // Ё / `~
            {"OemPlus", "="}, {"OemMinus", "-"},              // +/= и -/_  (основная)
            {"OemQuestion", "/"}, {"Oem2", "/"},              // ?/
            {"OemComma", ","}, {"OemPeriod", "."},             // , .
            {"OemSemicolon", ";"}, {"Oem1", ";"},             // ;:
            {"OemQuotes", "'"}, {"Oem7", "'"},                // '"
            {"OemOpenBrackets", "["}, {"Oem4", "["},          // [{
            {"OemCloseBrackets", "]"}, {"Oem6", "]"},         // ]}
            
            // === Numpad (NumLock ON) ===
            {"NumPad0", "Numpad0"}, {"NumPad1", "Numpad1"}, {"NumPad2", "Numpad2"},
            {"NumPad3", "Numpad3"}, {"NumPad4", "Numpad4"}, {"NumPad5", "Numpad5"},
            {"NumPad6", "Numpad6"}, {"NumPad7", "Numpad7"}, {"NumPad8", "Numpad8"},
            {"NumPad9", "Numpad9"},
            {"Multiply", "NumpadMult"}, {"Divide", "NumpadDiv"},
            {"Add", "NumpadAdd"}, {"Subtract", "NumpadSub"},
            {"Decimal", "NumpadDot"},
            
            // === Lock keys ===
            {"Capital", "CapsLock"}, {"Scroll", "ScrollLock"}, {"NumLock", "NumLock"},
            
            // === Navigation ===
            {"Prior", "PgUp"}, {"Next", "PgDn"},
            {"Return", "Enter"},
            
            // === Special keys ===
            {"Back", "Backspace"}, {"Snapshot", "PrintScreen"},
            {"Cancel", "CtrlBreak"}, {"Pause", "Pause"},
            {"Apps", "AppsKey"}, {"Sleep", "Sleep"},
            {"Help", "Help"},
            
            // === Windows keys ===
            {"LWin", "LWin"}, {"RWin", "RWin"},

            // === Browser/Multimedia ===
            {"BrowserBack", "Browser_Back"}, {"BrowserForward", "Browser_Forward"},
            {"BrowserRefresh", "Browser_Refresh"}, {"BrowserStop", "Browser_Stop"},
            {"BrowserSearch", "Browser_Search"}, {"BrowserFavorites", "Browser_Favorites"},
            {"BrowserHome", "Browser_Home"},
            {"VolumeMute", "Volume_Mute"}, {"VolumeDown", "Volume_Down"}, {"VolumeUp", "Volume_Up"},
            {"MediaNextTrack", "Media_Next"}, {"MediaPreviousTrack", "Media_Prev"},
            {"MediaStop", "Media_Stop"}, {"MediaPlayPause", "Media_Play_Pause"},
            {"LaunchMail", "Launch_Mail"}, {"SelectMedia", "Launch_Media"},
            {"LaunchApplication1", "Launch_App1"}, {"LaunchApplication2", "Launch_App2"},
        };

        // Display names for the UI (friendly labels)
        public static readonly Dictionary<string, string> WpfToDisplayKey = new(StringComparer.OrdinalIgnoreCase) {
            {"Oem5", "\\"}, {"OemPipe", "\\"},
            {"Oem3", "Ё"}, {"OemTilde", "Ё"},
            {"OemPlus", "="}, {"OemMinus", "-"},
            {"OemQuestion", "/"}, {"Oem2", "/"},
            {"OemComma", ","}, {"OemPeriod", "."},
            {"OemSemicolon", ";"}, {"Oem1", ";"},
            {"OemQuotes", "'"}, {"Oem7", "'"},
            {"OemOpenBrackets", "["}, {"Oem4", "["},
            {"OemCloseBrackets", "]"}, {"Oem6", "]"},
            {"Multiply", "Num *"}, {"Divide", "Num /"},
            {"Add", "Num +"}, {"Subtract", "Num -"},
            {"Decimal", "Num ."}, {"Capital", "CapsLock"},
            {"Prior", "PgUp"}, {"Next", "PgDn"},
            {"Return", "Enter"}, {"Back", "Backspace"},
            {"Snapshot", "PrintScreen"}, {"Apps", "Menu"},
        };
    }
}
