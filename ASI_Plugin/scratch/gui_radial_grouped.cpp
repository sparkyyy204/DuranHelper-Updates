void Gui::RenderRadialMenu() {
    ImDrawList* dl = ImGui::GetForegroundDrawList();
    ImVec2 ds = ImGui::GetIO().DisplaySize;
    float cx = ds.x * 0.5f, cy = ds.y * 0.5f;
    
    ImVec2 mp = ImGui::GetIO().MousePos;
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
        double grpStartOffset = 315.0; // SVG match
        
        // --- Determine Hovered ---
        if (dist > innerR && radialSelectedGroup == -1) {
            double normAngle = mouseAngle - grpStartOffset;
            while (normAngle < 0) normAngle += 360.0;
            while (normAngle >= 360.0) normAngle -= 360.0;
            radialHoveredGroup = (int)(normAngle / grpStep);
            if (radialHoveredGroup >= nGroups) radialHoveredGroup = nGroups - 1;
        } else if (dist > groupR && radialSelectedGroup != -1) {
            int nBinds = radialGroups[radialSelectedGroup].sectorCount;
            if (nBinds > 0) {
                double activeGroupMid = grpStartOffset + radialSelectedGroup * grpStep + grpStep / 2.0;
                double fanTotal = nBinds * 60.0;
                double fanStart = activeGroupMid - fanTotal / 2.0;
                
                double normAngle = mouseAngle - fanStart;
                while (normAngle < -180.0) normAngle += 360.0;
                while (normAngle >= 180.0) normAngle -= 360.0;
                
                // Only hover if within the fan
                if (normAngle >= 0 && normAngle <= fanTotal) {
                    radialHoveredSector = (int)(normAngle / 60.0);
                    if (radialHoveredSector >= nBinds) radialHoveredSector = nBinds - 1;
                }
            }
        } else if (dist > innerR && dist <= groupR && radialSelectedGroup != -1) {
            double normAngle = mouseAngle - grpStartOffset;
            while (normAngle < 0) normAngle += 360.0;
            while (normAngle >= 360.0) normAngle -= 360.0;
            radialHoveredGroup = (int)(normAngle / grpStep);
            if (radialHoveredGroup >= nGroups) radialHoveredGroup = nGroups - 1;
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
            dl->AddLine(lineStart, lineEnd, divCol, isThickDiv ? 2.7f : 2.2f);
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
            
            drawDonutSector(a1, a2, innerR, groupR, groupColor, divColor, hov, sel);

            // Group Name
            double midA = a1 + grpStep / 2.0;
            float tx = cx + 96.0f * cosf((float)(midA * 3.14159 / 180.0));
            float ty = cy + 96.0f * sinf((float)(midA * 3.14159 / 180.0));
            
            std::string gName = (i < radialGroups.size()) ? radialGroups[i].name : ("ГР " + std::to_string(i+1));
            float fSize = gName.length() > 8 ? 13.0f : 14.0f;
            ImVec2 ts = fontSegoeBold14->CalcTextSizeA(fSize, FLT_MAX, 0.0f, gName.c_str());
            ImU32 tCol = sel ? C_WHITE : ((C_WHITE & 0x00FFFFFF) | (220 << 24));
            dl->AddText(fontSegoeBold14, fSize, ImVec2(tx - ts.x * 0.5f, ty - ts.y * 0.5f), tCol, gName.c_str());

            // Active Group Arrows
            if (sel) {
                float arrowX = cx + 136.0f * cosf((float)(midA * 3.14159 / 180.0));
                float arrowY = cy + 136.0f * sinf((float)(midA * 3.14159 / 180.0));
                
                // We rotate the arrow polygon based on midA
                float ca = cosf((float)(midA * 3.14159 / 180.0 + 3.14159 / 2.0));
                float sa = sinf((float)(midA * 3.14159 / 180.0 + 3.14159 / 2.0));
                auto rotPt = [&](float px, float py) {
                    return ImVec2(arrowX + px * ca - py * sa, arrowY + px * sa + py * ca);
                };

                ImVec2 arrow1[3] = { rotPt(-8, -5), rotPt(0, 5), rotPt(8, -5) };
                dl->AddConvexPolyFilled(arrow1, 3, (C_GOLD & 0x00FFFFFF) | (153 << 24));
                ImVec2 arrow2[3] = { rotPt(-8, -12), rotPt(0, -2), rotPt(8, -12) };
                dl->AddConvexPolyFilled(arrow2, 3, (C_GOLD & 0x00FFFFFF) | (77 << 24));
            }
        }

        // ═══ OUTER RING (Sectors) ═══
        if (radialSelectedGroup != -1) {
            int nBinds = radialGroups[radialSelectedGroup].sectorCount;
            auto& activeSectors = radialGroups[radialSelectedGroup].sectors;
            
            double activeGroupMid = grpStartOffset + radialSelectedGroup * grpStep + grpStep / 2.0;
            double fanTotal = nBinds * 60.0;
            double fanStart = activeGroupMid - fanTotal / 2.0;

            for (int i = 0; i < nBinds; i++) {
                double a1 = fanStart + i * 60.0;
                double a2 = a1 + 60.0;
                bool hov = (i == radialHoveredSector);
                
                ImU32 secCol = hov ? ((C_GOLD & 0x00FFFFFF) | (68 << 24)) : themeBg;
                ImU32 divCol = hov ? ((C_GOLD & 0x00FFFFFF) | (230 << 24)) : ((C_LINE & 0x00FFFFFF) | (210 << 24));

                // 162 to 255
                drawDonutSector(a1, a2, 162.0f, 255.0f, secCol, divCol, hov, hov);

                if (hov) {
                    double rA1 = a1 * 3.14159 / 180.0, rA2 = a2 * 3.14159 / 180.0;
                    dl->AddLine(ImVec2(cx + 162.0f * cosf(rA1), cy + 162.0f * sinf(rA1)), 
                                ImVec2(cx + 255.0f * cosf(rA1), cy + 255.0f * sinf(rA1)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
                    dl->AddLine(ImVec2(cx + 162.0f * cosf(rA2), cy + 162.0f * sinf(rA2)), 
                                ImVec2(cx + 255.0f * cosf(rA2), cy + 255.0f * sinf(rA2)), (C_GOLD & 0x00FFFFFF) | (230 << 24), 2.5f);
                }

                double midA = a1 + 30.0;
                float tx = cx + 208.0f * cosf((float)(midA * 3.14159 / 180.0));
                float ty = cy + 208.0f * sinf((float)(midA * 3.14159 / 180.0));
                
                std::string bName = (i < activeSectors.size()) ? activeSectors[i].bindName : "";
                if (bName.empty()) bName = "Бинд " + std::to_string(i + 1);

                ImVec2 ts = fontSegoeBold12->CalcTextSizeA(12.0f, FLT_MAX, 0.0f, bName.c_str());
                ImU32 tCol = hov ? C_WHITE : ((C_WHITE & 0x00FFFFFF) | (220 << 24));
                dl->AddText(fontSegoeBold12, 12.0f, ImVec2(tx - ts.x * 0.5f, ty - 12.0f - ts.y * 0.5f), tCol, bName.c_str());
                
                if (i < activeSectors.size() && !activeSectors[i].bindId.empty()) {
                    ImU32 iconCol = hov ? C_GOLD : ((C_GOLD & 0x00FFFFFF) | (180 << 24));
                    DrawRadialIcon(dl, ImVec2(tx, ty + 10.0f), 17.0f, activeSectors[i].icon, iconCol);
                }
            }

            // Outer decorative arc
            if (nBinds < 6) {
                const int steps = 40;
                double radA1 = fanStart * 3.14159 / 180.0;
                double radA2 = (fanStart + fanTotal) * 3.14159 / 180.0;
                for (int s = 0; s < steps; s++) {
                    double a = radA1 + (radA2 - radA1) * s / steps;
                    double nextA = radA1 + (radA2 - radA1) * (s + 1) / steps;
                    dl->AddLine(ImVec2(cx + 255.0f * cosf((float)a), cy + 255.0f * sinf((float)a)),
                                ImVec2(cx + 255.0f * cosf((float)nextA), cy + 255.0f * sinf((float)nextA)), 
                                (C_LINE & 0x00FFFFFF) | (153 << 24), 2.0f);
                }
                
                // + indicators at edges
                float p1x = cx + 208.5f * cosf((float)radA1); float p1y = cy + 208.5f * sinf((float)radA1);
                dl->AddLine(ImVec2(p1x-4, p1y), ImVec2(p1x+4, p1y), (C_GOLD & 0x00FFFFFF) | (200 << 24), 2.0f);
                dl->AddLine(ImVec2(p1x, p1y-4), ImVec2(p1x, p1y+4), (C_GOLD & 0x00FFFFFF) | (200 << 24), 2.0f);

                float p2x = cx + 208.5f * cosf((float)radA2); float p2y = cy + 208.5f * sinf((float)radA2);
                dl->AddLine(ImVec2(p2x-4, p2y), ImVec2(p2x+4, p2y), (C_GOLD & 0x00FFFFFF) | (200 << 24), 2.0f);
                dl->AddLine(ImVec2(p2x, p2y-4), ImVec2(p2x, p2y+4), (C_GOLD & 0x00FFFFFF) | (200 << 24), 2.0f);
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
        dl->AddLine(lineStart, lineEnd, dividerColor, hovered ? 2.7f : 2.2f);

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
        dl->AddLine(lineStart1, lineEnd1, (C_GOLD & 0x00FFFFFF) | (245 << 24), 2.9f);
        dl->AddLine(lineStart2, lineEnd2, (C_GOLD & 0x00FFFFFF) | (245 << 24), 2.9f);
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
