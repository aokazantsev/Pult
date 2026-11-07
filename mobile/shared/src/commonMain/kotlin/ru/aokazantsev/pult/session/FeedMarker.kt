package ru.aokazantsev.pult.session

sealed interface FeedMarker {

    data object Start : FeedMarker

    data class Between(val after: Long, val missing: Long) : FeedMarker

    data object End : FeedMarker
}
