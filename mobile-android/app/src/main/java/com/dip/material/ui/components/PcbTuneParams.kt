package com.dip.material.ui.components

/**
 * PCB 刻印 Data Matrix 识别调参集合（移植自 machine_check）。
 * 仅保留 AUTO + PCB 通道所需参数，ZXing 相关字段已移除。
 */
data class PcbTuneParams(
    /** Sauvola 局部窗口边长（奇数更佳），默认 15 */
    val sauvolaWindowSize: Int = 15,
    /** Sauvola 对比度系数，刻印码建议 0.1~0.3，默认 0.5 */
    val sauvolaK: Double = 0.5,
    /** Sauvola 动态范围常数，默认 128 */
    val sauvolaR: Double = 128.0,
    /** 对比拉伸前裁掉的百分位(0~50)：抗单点高光/坏点，默认 0 */
    val stretchClipPct: Double = 0.0,
    /** 对比拉伸后的 gamma(1.0=线性)，默认 1.0 */
    val stretchGamma: Double = 1.0,
    /** ROI 裁剪比例（沿传感器宽方向，对应屏幕竖向）：默认 0.45 */
    val roiWidthFraction: Float = 0.45f,
    /** ROI 裁剪比例（沿传感器高方向，对应屏宽方向）：默认 0.95 */
    val roiHeightFraction: Float = 0.95f
)
