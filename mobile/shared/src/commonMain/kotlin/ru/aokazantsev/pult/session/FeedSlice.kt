package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.FeedEntry

data class FeedSlice(val entries: List<FeedEntry>, val markers: List<FeedMarker>) {

    companion object {
        val Empty = FeedSlice(emptyList(), emptyList())
    }
}
