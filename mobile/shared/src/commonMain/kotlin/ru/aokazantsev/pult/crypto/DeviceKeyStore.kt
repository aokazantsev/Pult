package ru.aokazantsev.pult.crypto

interface DeviceKeyStore {
    fun create(pair: String): CreatedKeys

    fun open(pair: String): DeviceKeys?

    fun delete(pair: String)

    fun deleteAllExcept(pair: String?)
}
