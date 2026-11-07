package ru.aokazantsev.pult.render

sealed interface RichBlock {

    data class Paragraph(val spans: List<RichSpan>) : RichBlock

    data class Heading(val spans: List<RichSpan>) : RichBlock

    data class CodeBlock(val text: String) : RichBlock

    data class ListItem(val depth: Int, val marker: String, val spans: List<RichSpan>) : RichBlock

    data class Quote(val depth: Int, val spans: List<RichSpan>) : RichBlock
}
