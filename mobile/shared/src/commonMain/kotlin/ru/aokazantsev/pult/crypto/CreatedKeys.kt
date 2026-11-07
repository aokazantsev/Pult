package ru.aokazantsev.pult.crypto

class CreatedKeys(val keys: DeviceKeys, val signProtection: KeyProtection, val agreeProtection: KeyProtection) {
    val isHardware: Boolean
        get() = signProtection == KeyProtection.Hardware && agreeProtection == KeyProtection.Hardware
}
