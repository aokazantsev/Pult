package ru.aokazantsev.pult.ui.render

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.withLink
import androidx.compose.ui.unit.dp
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.render.LinkTarget
import ru.aokazantsev.pult.render.MarkdownParser
import ru.aokazantsev.pult.render.RichBlock
import ru.aokazantsev.pult.render.RichSpan
import ru.aokazantsev.pult.render.TextSanitizer

val LocalLinkHandler = compositionLocalOf<(LinkTarget) -> Unit> { {} }

@Composable
fun UntrustedPlain(
    text: String,
    modifier: Modifier = Modifier,
    style: TextStyle = PultTheme.typography.bodyMedium,
    color: Color = Color.Unspecified,
    maxLines: Int = Int.MAX_VALUE,
) {
    val safe = remember(text) { TextSanitizer.sanitize(text) }
    PultText(text = safe, modifier = modifier, style = style, color = color, maxLines = maxLines)
}

@Composable
fun UntrustedLine(
    text: String,
    modifier: Modifier = Modifier,
    style: TextStyle = PultTheme.typography.bodyMedium,
    color: Color = Color.Unspecified,
    maxLines: Int = 1,
) {
    val safe = remember(text) { TextSanitizer.oneLine(text) }
    PultText(text = safe, modifier = modifier, style = style, color = color, maxLines = maxLines)
}

@Composable
fun UntrustedMono(
    text: String,
    modifier: Modifier = Modifier,
    color: Color = Color.Unspecified,
    maxLines: Int = Int.MAX_VALUE,
) {
    val safe = remember(text) { TextSanitizer.sanitize(text) }
    PultText(text = safe, modifier = modifier, style = PultTheme.typography.code, color = color, maxLines = maxLines)
}

@Composable
fun UntrustedMarkdown(text: String, modifier: Modifier = Modifier) {
    val blocks = remember(text) { MarkdownParser.parse(TextSanitizer.sanitize(text)) }
    val onLink = LocalLinkHandler.current
    val linkColor = PultTheme.colors.primary
    val codeStyle = PultTheme.typography.code.toSpanStyle()
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.s)) {
        blocks.forEach { block ->
            when (block) {
                is RichBlock.Paragraph -> RichLine(block.spans, linkColor, codeStyle, onLink)
                is RichBlock.Heading -> RichLine(block.spans, linkColor, codeStyle, onLink, style = PultTheme.typography.titleMedium)
                is RichBlock.CodeBlock -> PultText(
                    text = block.text,
                    style = PultTheme.typography.code,
                    modifier = Modifier.fillMaxWidth().padding(vertical = PultTheme.spacing.xs),
                )
                is RichBlock.ListItem -> Row(modifier = Modifier.padding(start = INDENT.dp * block.depth)) {
                    PultText(text = block.marker)
                    Spacer(Modifier.width(PultTheme.spacing.s))
                    RichLine(block.spans, linkColor, codeStyle, onLink)
                }
                is RichBlock.Quote -> Row(modifier = Modifier.padding(start = INDENT.dp * (block.depth - 1))) {
                    PultText(text = "│", color = PultTheme.colors.outline)
                    Spacer(Modifier.width(PultTheme.spacing.s))
                    RichLine(block.spans, linkColor, codeStyle, onLink, color = PultTheme.colors.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
private fun RichLine(
    spans: List<RichSpan>,
    linkColor: Color,
    codeStyle: SpanStyle,
    onLink: (LinkTarget) -> Unit,
    style: TextStyle = PultTheme.typography.bodyMedium,
    color: Color = Color.Unspecified,
) {
    val annotated = remember(spans, linkColor, codeStyle) {
        buildAnnotatedString {
            spans.forEach { span ->
                val base = SpanStyle(
                    fontWeight = if (span.bold) FontWeight.Bold else null,
                    fontStyle = if (span.italic) FontStyle.Italic else null,
                )
                val link = span.link
                when {
                    link != null -> withLink(
                        LinkAnnotation.Clickable(
                            tag = link.url,
                            styles = TextLinkStyles(SpanStyle(color = linkColor, textDecoration = TextDecoration.Underline)),
                            linkInteractionListener = { onLink(link) },
                        ),
                    ) { append(span.text) }
                    span.code -> {
                        val start = length
                        append(span.text)
                        addStyle(codeStyle, start, length)
                    }
                    else -> {
                        val start = length
                        append(span.text)
                        addStyle(base, start, length)
                    }
                }
            }
        }
    }
    PultText(text = annotated, style = style, color = color)
}

@Composable
fun ProvideLinkHandler(onLink: (LinkTarget) -> Unit, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalLinkHandler provides onLink, content = content)
}

private const val INDENT = 16
