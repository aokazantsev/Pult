package ru.aokazantsev.pult.navigation

class ScanExit {

    private var returned = false

    fun onPairedChanged(paired: Boolean): Boolean {
        if (!paired) {
            returned = false
            return false
        }
        if (returned) return false
        returned = true
        return true
    }
}
