package ru.aokazantsev.pult.access

interface DeviceAuthenticator {

    fun isDeviceSecure(): Boolean

    fun authenticate(onResult: (AuthResult) -> Unit)
}
