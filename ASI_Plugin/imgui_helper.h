#pragma once
#include "imgui.h"

namespace ImGuiHelper {
    // Плавная интерполяция между значениями
    inline float Lerp(float a, float b, float t) {
        return a + (b - a) * t;
    }

    // Вспомогательная функция для плавных цветов
    inline ImVec4 FadeColor(ImVec4 color, float alpha) {
        return ImVec4(color.x, color.y, color.z, color.w * alpha);
    }
}
