package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.PcMessage

class SnapshotAssembler {

    private val parts = ArrayList<PcMessage.Snapshot>()

    fun accept(part: PcMessage.Snapshot): SnapshotStep {
        if (part.part == 1L) {
            parts.clear()
            parts.add(part)
        } else {
            val first = parts.firstOrNull()
            if (first == null || part.part != parts.size + 1L || part.snapshot != first.snapshot) {
                parts.clear()
                return SnapshotStep.Discarded
            }
            parts.add(part)
        }
        if (!part.last) return SnapshotStep.Incomplete
        val complete = parts.toList()
        parts.clear()
        return SnapshotStep.Complete(complete)
    }

    fun reset() {
        parts.clear()
    }
}
