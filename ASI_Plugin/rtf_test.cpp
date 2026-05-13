#include <iostream>
#include <string>
#include <vector>

std::vector<unsigned int> ParseColorTbl(std::string tbl) {
    std::vector<unsigned int> colors;
    if (!tbl.empty() && tbl[0] == ';') {
        colors.push_back(0xFFFFFFFF);
        tbl = tbl.substr(1);
    }
    std::string currentItem = "";
    for (char c : tbl) {
        if (c == ';') {
            int r = 255, g = 255, b = 255;
            size_t rp = currentItem.find("\\red");
            if (rp != std::string::npos) r = std::stoi(currentItem.substr(rp + 4));
            size_t gp = currentItem.find("\\green");
            if (gp != std::string::npos) g = std::stoi(currentItem.substr(gp + 6));
            size_t bp = currentItem.find("\\blue");
            if (bp != std::string::npos) b = std::stoi(currentItem.substr(bp + 5));
            colors.push_back(0xFF000000 | ((b & 0xFF) << 16) | ((g & 0xFF) << 8) | (r & 0xFF));
            currentItem.clear();
        } else {
            currentItem += c;
        }
    }
    return colors;
}

int main() {
    auto colors = ParseColorTbl("\\red0\\green0\\blue0;\\red255\\green255\\blue255;\\red255\\green123\\blue114;");
    for (size_t i = 0; i < colors.size(); i++) {
        printf("Color %zu: %08X\n", i, colors[i]);
    }
    return 0;
}