package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.crypto.DeviceKeys
import ru.aokazantsev.pult.pairing.StoredPair
import ru.aokazantsev.pult.protocol.DirectionKeys

class ActiveLink(
    val pair: StoredPair,
    val keys: DeviceKeys,
    val direction: DirectionKeys,
)
