package com.dip.material

import android.app.Application
import com.dip.material.utils.ScanSoundManager

class DIPApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        ScanSoundManager.init(this)
    }
}
