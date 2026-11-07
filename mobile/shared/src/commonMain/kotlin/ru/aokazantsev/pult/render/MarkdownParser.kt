package ru.aokazantsev.pult.render

object MarkdownParser {

    private const val FENCE = "```"
    private const val MAX_DEPTH = 4
    private val heading = Regex("^(#{1,6})\\s+(.*)$")
    private val listItem = Regex("^( *)([-*]|\\d+\\.)\\s+(.*)$")
    private val quote = Regex("^((?:\\s*>)+)\\s?(.*)$")

    fun parse(text: String): List<RichBlock> {
        val blocks = ArrayList<RichBlock>()
        for (segment in splitFences(text)) {
            if (segment.code) {
                blocks.add(RichBlock.CodeBlock(codeBody(segment.text)))
            } else {
                parseProse(segment.text, blocks)
            }
        }
        return blocks
    }

    private fun splitFences(text: String): List<Segment> {
        val positions = ArrayList<Int>()
        var from = 0
        while (true) {
            val index = text.indexOf(FENCE, from)
            if (index < 0) break
            positions.add(index)
            from = index + FENCE.length
        }
        val pairs = positions.size / 2
        if (pairs == 0) return listOf(Segment(text, code = false))
        val segments = ArrayList<Segment>()
        var cursor = 0
        for (pair in 0 until pairs) {
            val open = positions[pair * 2]
            val close = positions[pair * 2 + 1]
            if (open > cursor) segments.add(Segment(text.substring(cursor, open), code = false))
            segments.add(Segment(text.substring(open + FENCE.length, close), code = true))
            cursor = close + FENCE.length
        }
        if (cursor < text.length) segments.add(Segment(text.substring(cursor), code = false))
        return segments
    }

    private fun codeBody(raw: String): String {
        val firstBreak = raw.indexOf('\n')
        val head = if (firstBreak >= 0) raw.substring(0, firstBreak) else raw
        val body = if (firstBreak >= 0 && head.isNotEmpty() && head.none { it.isWhitespace() }) raw.substring(firstBreak + 1) else raw
        return body.removePrefix("\n").removeSuffix("\n")
    }

    private fun parseProse(text: String, blocks: MutableList<RichBlock>) {
        val paragraph = ArrayList<String>()
        fun flush() {
            if (paragraph.isNotEmpty()) {
                blocks.add(RichBlock.Paragraph(InlineParser.parse(paragraph.joinToString("\n"))))
                paragraph.clear()
            }
        }
        for (line in text.split('\n')) {
            val headingMatch = heading.matchEntire(line)
            val listMatch = listItem.matchEntire(line)
            val quoteMatch = quote.matchEntire(line)
            when {
                line.isBlank() -> flush()
                headingMatch != null -> {
                    flush()
                    blocks.add(RichBlock.Heading(InlineParser.parse(headingMatch.groupValues[2])))
                }
                listMatch != null -> {
                    flush()
                    val depth = minOf(listMatch.groupValues[1].length / 2, MAX_DEPTH - 1)
                    val marker = listMatch.groupValues[2].let { if (it == "-" || it == "*") "•" else it }
                    blocks.add(RichBlock.ListItem(depth, marker, InlineParser.parse(listMatch.groupValues[3])))
                }
                quoteMatch != null -> {
                    flush()
                    val depth = minOf(quoteMatch.groupValues[1].count { it == '>' }, MAX_DEPTH)
                    blocks.add(RichBlock.Quote(depth, InlineParser.parse(quoteMatch.groupValues[2])))
                }
                else -> paragraph.add(line)
            }
        }
        flush()
    }

    private class Segment(val text: String, val code: Boolean)
}
