package ru.aokazantsev.pult.protocol.message

internal class MessageRejected(val reason: String) : Exception(reason)
