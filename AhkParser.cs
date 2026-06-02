using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FSB_helper_C__
{
    public class AhkParseResult
    {
        public List<BindItem> Binds { get; set; } = new List<BindItem>();
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
    }

    public static class AhkParser
    {
        public static AhkParseResult Parse(string filePath)
        {
            var result = new AhkParseResult();
            if (!File.Exists(filePath)) return result;

            string[] lines = File.ReadAllLines(filePath);
            BindItem currentBind = null;
            int bindCounter = 1;

            foreach (string line in lines)
            {
                string tLine = line.Trim();

                // Skip empty lines or pure AHK comments
                if (string.IsNullOrEmpty(tLine) || tLine.StartsWith(";"))
                    continue;

                // Detect variable assignment: VarName = Value or VarName := Value
                var varMatch = Regex.Match(tLine, @"^([a-zA-Z0-9_а-яА-Я]+)\s*(?::?=)\s*(.+)$");
                if (varMatch.Success && !tLine.EndsWith("::"))
                {
                    string varName = $"*{varMatch.Groups[1].Value.Trim()}*";
                    string varValue = varMatch.Groups[2].Value.Trim().Trim('"'); // remove quotes if any
                    result.Variables[varName] = varValue;
                    continue;
                }

                // Detect start of a bind: e.g., F9:: or ^+A:: or :*?:/rep::
                // Actually, hotstrings like :*?:/rep:: are also binds.
                // We can match any line containing :: that isn't a variable assignment,
                // or just match standard hotkeys and hotstrings
                var bindMatch = Regex.Match(tLine, @"^(.+)::$");
                if (bindMatch.Success && !tLine.StartsWith("::"))
                {
                    currentBind = new BindItem
                    {
                        id = Guid.NewGuid().ToString(),
                        name = $"Импорт AHK #{bindCounter}",
                        key = ConvertAhkKeyToLauncherKey(bindMatch.Groups[1].Value.Trim()),
                        group = "ВСЕ", // As requested, assign to default group
                        active = true,
                        steps = new List<BindStep>(),
                        isAuto = false
                    };
                    bindCounter++;
                    
                    // If it's a hotstring like :*?:/rep::/report{space}, the action is on the same line!
                    var parts = tLine.Split(new[] { "::" }, StringSplitOptions.None);
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        // It's a single-line hotstring, e.g. :*?:/rep::/report{space}
                        string content = ProcessAhkText(parts[2], result.Variables);
                        bool isEnter = false;
                        if (content.EndsWith("{enter}", StringComparison.OrdinalIgnoreCase))
                        {
                            isEnter = true;
                            content = content.Substring(0, content.Length - 7).TrimEnd();
                        }
                        
                        currentBind.steps.Add(new BindStep
                        {
                            Index = currentBind.steps.Count,
                            action = "CHAT",
                            value = content,
                            desc = "ЧАТ",
                            isEnter = isEnter,
                            ColorCode = "#1f6feb"
                        });
                        result.Binds.Add(currentBind);
                        currentBind = null;
                    }
                    continue;
                }

                if (currentBind == null) continue;

                // End of bind block
                if (tLine.Equals("Return", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentBind.steps.Count > 0)
                    {
                        result.Binds.Add(currentBind);
                    }
                    currentBind = null;
                    continue;
                }

                // Match SendInput or Send
                var sendMatch = Regex.Match(tLine, @"^(?i)(?:SendInput|SendPlay|SendEvent|Send)\b\s*,?\s*(.+)$");
                if (sendMatch.Success)
                {
                    string content = sendMatch.Groups[1].Value.Trim();

                    // Check if it's a single keypress like {Esc} or {Space}
                    var singleKeyMatch = Regex.Match(content, @"^\{([a-zA-Z0-9]+)\}$");
                    if (singleKeyMatch.Success)
                    {
                        string k = singleKeyMatch.Groups[1].Value;
                        if (k.Equals("ESC", StringComparison.OrdinalIgnoreCase)) k = "Escape";
                        else if (k.Equals("ENTER", StringComparison.OrdinalIgnoreCase)) k = "Enter";
                        else if (k.Equals("SPACE", StringComparison.OrdinalIgnoreCase)) k = "Space";
                        else if (k.Equals("UP", StringComparison.OrdinalIgnoreCase)) k = "Up";
                        else if (k.Equals("DOWN", StringComparison.OrdinalIgnoreCase)) k = "Down";
                        else if (k.Equals("LEFT", StringComparison.OrdinalIgnoreCase)) k = "Left";
                        else if (k.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)) k = "Right";
                        else if (k.Equals("TAB", StringComparison.OrdinalIgnoreCase)) k = "Tab";
                        else if (k.StartsWith("F", StringComparison.OrdinalIgnoreCase)) k = k.ToUpper();
                        else if (k.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase)) k = "NumPad" + k.Substring(6);
                        else if (k.Length == 1 && char.IsLetterOrDigit(k[0])) k = k.ToUpper();

                        currentBind.steps.Add(new BindStep
                        {
                            Index = currentBind.steps.Count,
                            action = "PRESS",
                            value = k,
                            desc = "КНОПКА",
                            isEnter = false,
                            ColorCode = "#ff7b72"
                        });
                        continue;
                    }

                    content = ProcessAhkText(content, result.Variables);

                    bool isEnter = false;
                    if (content.EndsWith("{enter}", StringComparison.OrdinalIgnoreCase))
                    {
                        isEnter = true;
                        content = content.Substring(0, content.Length - 7).TrimEnd();
                    }

                    currentBind.steps.Add(new BindStep
                    {
                        Index = currentBind.steps.Count,
                        action = "CHAT",
                        value = content,
                        desc = "ЧАТ",
                        isEnter = isEnter,
                        ColorCode = "#1f6feb"
                    });
                    continue;
                }

                // Match Sleep
                var sleepMatch = Regex.Match(tLine, @"^(?i)Sleep\s*,?\s*(\d+)$");
                if (sleepMatch.Success)
                {
                    int delay;
                    if (int.TryParse(sleepMatch.Groups[1].Value, out delay))
                    {
                        if (delay != 1000)
                        {
                            currentBind.steps.Add(new BindStep
                            {
                                Index = currentBind.steps.Count,
                                action = "WAIT",
                                value = delay.ToString(),
                                desc = "ПАУЗА",
                                isEnter = false,
                                ColorCode = "#d2a65e"
                            });
                        }
                    }
                    continue;
                }
            }

            if (currentBind != null && currentBind.steps.Count > 0)
            {
                result.Binds.Add(currentBind);
            }

            return result;
        }

        private static string ConvertAhkKeyToLauncherKey(string ahkKey)
        {
            if (string.IsNullOrWhiteSpace(ahkKey)) return "НЕТ";

            if (ahkKey.StartsWith(":")) return "НЕТ"; // Hotstrings

            string result = "";
            bool hasAlt = false;
            bool hasCtrl = false;
            bool hasShift = false;
            bool hasWin = false;

            while (ahkKey.Length > 0 && (ahkKey[0] == '!' || ahkKey[0] == '^' || ahkKey[0] == '+' || ahkKey[0] == '#' || ahkKey[0] == '<' || ahkKey[0] == '>'))
            {
                char c = ahkKey[0];
                if (c == '!') hasAlt = true;
                else if (c == '^') hasCtrl = true;
                else if (c == '+') hasShift = true;
                else if (c == '#') hasWin = true;
                ahkKey = ahkKey.Substring(1);
            }

            if (hasCtrl) result += "Ctrl + ";
            if (hasAlt) result += "Alt + ";
            if (hasShift) result += "Shift + ";
            if (hasWin) result += "Win + ";

            string key = ahkKey.ToUpper();

            if (key.Equals("RSHIFT", StringComparison.OrdinalIgnoreCase)) key = "RShift";
            else if (key.Equals("LSHIFT", StringComparison.OrdinalIgnoreCase)) key = "LShift";
            else if (key.Equals("RCTRL", StringComparison.OrdinalIgnoreCase)) key = "RCtrl";
            else if (key.Equals("LCTRL", StringComparison.OrdinalIgnoreCase)) key = "LCtrl";
            else if (key.Equals("RALT", StringComparison.OrdinalIgnoreCase)) key = "RAlt";
            else if (key.Equals("LALT", StringComparison.OrdinalIgnoreCase)) key = "LAlt";
            else if (key.StartsWith("XBUTTON", StringComparison.OrdinalIgnoreCase)) key = "XButton" + key.Substring(7);
            else if (key.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase)) key = "NumPad" + key.Substring(6);
            else if (key.Length == 1 && char.IsLetterOrDigit(key[0])) key = key.ToUpper();
            
            result += key;
            return string.IsNullOrEmpty(result) ? "НЕТ" : result;
        }

        private static string ProcessAhkText(string input, Dictionary<string, string> variables)
        {
            // Check for {F6} prefix
            if (input.StartsWith("{F6}", StringComparison.OrdinalIgnoreCase))
            {
                input = input.Substring(4).TrimStart();
            }

            // Replace {space} with actual space
            input = Regex.Replace(input, @"\{space\}", " ", RegexOptions.IgnoreCase);

            // Replace AHK variables %var% with launcher variables *var*, handling case insensitivity
            input = Regex.Replace(input, @"%([a-zA-Z0-9_а-яА-Я]+)%", match =>
            {
                string varName = match.Groups[1].Value;
                string varKey = $"*{varName}*";
                foreach (var k in variables.Keys)
                {
                    if (k.Equals(varKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return k; // Use the exact case from the variable definition
                    }
                }
                return varKey; // Fallback to whatever they typed
            });

            return input;
        }
    }
}
