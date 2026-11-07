package ru.aokazantsev.pult.render

object MarkdownLinks {

    fun of(text: String, markdown: Boolean): List<LinkTarget> {
        if (!markdown) return emptyList()
        return MarkdownParser.parse(text).flatMap { block ->
            when (block) {
                is RichBlock.Paragraph -> block.spans
                is RichBlock.Heading -> block.spans
                is RichBlock.ListItem -> block.spans
                is RichBlock.Quote -> block.spans
                is RichBlock.CodeBlock -> emptyList()
            }
        }.mapNotNull { it.link }
    }
}
