package ru.aokazantsev.pult.access

class DebugLockOptionalAuthenticator(private val device: DeviceAuthenticator) : DeviceAuthenticator {

    override fun isDeviceSecure(): Boolean = true

    override fun authenticate(onResult: (AuthResult) -> Unit) {
        if (device.isDeviceSecure()) device.authenticate(onResult) else onResult(AuthResult.Success)
    }
}
