package com.dip.material.utils

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool
import com.dip.material.R

/**
 * 扫码音效管理 — SoundPool 播放 res/raw 中的音频文件（USAGE_ALARM 闹钟通道，最大音量）
 * ok.wav：升调两音 ding-ding（1318→1760Hz，正弦+二次谐波，明亮悦耳 = 对）
 * ng.wav：三连降调（660→494→330Hz，E5→B4→E4，陡方波含7次谐波，最狠警报 = 错）
 * 设计：升=对 / 降=错 的音频语法 + 陡方波高 RMS 使 NG 比 OK 更刺更醒目
 *
 * 使用前需在 Application.onCreate() 中调用 init(context)
 */
object ScanSoundManager {
    private var soundPool: SoundPool? = null
    private var okSoundId: Int = 0
    private var ngSoundId: Int = 0

    fun init(context: Context) {
        soundPool = SoundPool.Builder()
            .setMaxStreams(2)
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ALARM)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build()
            )
            .build()
        okSoundId = soundPool!!.load(context, R.raw.ok, 1)
        ngSoundId = soundPool!!.load(context, R.raw.ng, 1)
    }

    fun playSuccess() {
        try { soundPool?.play(okSoundId, 1f, 1f, 1, 0, 1f) } catch (_: Exception) {}
    }

    fun playError() {
        try { soundPool?.play(ngSoundId, 1f, 1f, 1, 0, 1f) } catch (_: Exception) {}
    }
}
