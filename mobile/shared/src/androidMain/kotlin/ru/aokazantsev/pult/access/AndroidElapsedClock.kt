package ru.aokazantsev.pult.access

import android.os.SystemClock

class AndroidElapsedClock : ElapsedClock {

    override fun elapsedMs(): Long = SystemClock.elapsedRealtime()
}
