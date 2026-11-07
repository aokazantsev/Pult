package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.PcMessage

sealed interface SnapshotStep {
    data object Incomplete : SnapshotStep

    data object Discarded : SnapshotStep

    data class Complete(val parts: List<PcMessage.Snapshot>) : SnapshotStep
}
