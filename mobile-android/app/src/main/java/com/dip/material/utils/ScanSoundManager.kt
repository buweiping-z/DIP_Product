package com.dip.material.utils

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool
import com.dip.material.R

/**
 * 扫码音效管理 — SoundPool 播放 res/raw 中的音频文件
 * ok.wav：800Hz 短高音 | ng.wav：400Hz 低沉音
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
