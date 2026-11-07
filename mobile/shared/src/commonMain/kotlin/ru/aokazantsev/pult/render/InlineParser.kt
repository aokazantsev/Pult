package ru.aokazantsev.pult.render

import ru.aokazantsev.pult.protocol.message.ProtocolText

internal object InlineParser {

    private val inlineCode = Regex("`[^`\\n]*`")
    private val markdownLink = Regex("\\[([^\\]]*)\\]\\(([^\\s()]+)\\)")
    private const val URL_STOPPERS = "<>\"'`"
    private const val URL_TRAILING = ".,;:!?)"

    fun parse(text: String): List<RichSpan> {
        val segments = segments(text)
        val spans = ArrayList<RichSpan>()
        var position = 0
        for (segment in segments) {
            if (segment.start < position) continue
            if (segment.start > position) spans.addAll(Emphasis.parse(text.substring(position, segment.start)))
            spans.add(segment.span)
            position = segment.end
        }
        if (position < text.length) spans.addAll(Emphasis.parse(text.substring(position)))
        return merge(spans)
    }

    private fun segments(text: String): List<Segment> {
        val found = ArrayList<Segment>()
        val codeRanges = ArrayList<IntRange>()
        for (match in inlineCode.findAll(text)) {
            if (match.value.length > 2) found.add(Segment(match.range.first, match.range.last + 1, RichSpan(match.value.substring(1, match.value.length - 1), code = true)))
            codeRanges.add(match.range)
        }
        val masked = StringBuilder(text)
        codeRanges.forEach { range -> for (index in range) masked[index] = ' ' }
        val maskedText = masked.toString()
        val linkRanges = ArrayList<IntRange>()
        for (match in markdownLink.findAll(maskedText)) {
            if (match.range.first > 0 && maskedText[match.range.first - 1] == '!') continue
            val label = match.groupValues[1]
            val url = match.groupValues[2]
            found.add(
                Segment(match.range.first, match.range.last + 1, RichSpan(label.ifEmpty { url }, link = LinkTarget(url))),
            )
            linkRanges.add(match.range)
        }
        var index = 0
        while (index < maskedText.length) {
            val scheme = schemeLengthAt(maskedText, index)
            if (scheme == 0) {
                index++
                continue
            }
            var end = index + scheme
            while (end < maskedText.length && !isUrlBreak(maskedText[end])) end++
            val raw = maskedText.substring(index, end)
            val url = raw.trimEnd { it in URL_TRAILING }
            if (linkRanges.none { index in it } && url.length > scheme) {
                found.add(Segment(index, index + url.length, RichSpan(url, link = LinkTarget(url))))
            }
            index = end
        }
        return found.sortedBy { it.start }
    }

    private fun schemeLengthAt(text: String, index: Int): Int = when {
        text.startsWith("https://", index) -> "https://".length
        text.startsWith("http://", index) -> "http://".length
        else -> 0
    }

    private fun isUrlBreak(char: Char): Boolean = ProtocolText.isWhiteSpace(char) || char in URL_STOPPERS

    private fun merge(spans: List<RichSpan>): List<RichSpan> {
        val merged = ArrayList<RichSpan>()
        for (span in spans) {
            val last = merged.lastOrNull()
            if (last != null && last.link == null && span.link == null &&
                last.bold == span.bold && last.italic == span.italic && last.code == span.code
            ) {
                merged[merged.lastIndex] = last.copy(text = last.text + span.text)
            } else {
                merged.add(span)
            }
        }
        return merged
    }

    private class Segment(val start: Int, val end: Int, val span: RichSpan)
}
