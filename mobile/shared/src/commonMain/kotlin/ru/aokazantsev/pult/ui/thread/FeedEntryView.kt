package ru.aokazantsev.pult.ui.thread

import androidx.compose.runtime.Composable
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.protocol.message.FeedEntry
import ru.aokazantsev.pult.protocol.message.PeerDirection
import ru.aokazantsev.pult.ui.render.UntrustedMarkdown
import ru.aokazantsev.pult.ui.render.UntrustedMono
import ru.aokazantsev.pult.ui.render.UntrustedPlain

@Composable
fun FeedEntryView(entry: FeedEntry) {
    when (entry) {
        is FeedEntry.User -> PultCard {
            SmallLabel(if (entry.viaPhone) "Вы, с телефона" else "Вы")
            UntrustedPlain(entry.text)
        }
        is FeedEntry.Assistant -> PultCard {
            SmallLabel("Агент")
            UntrustedMarkdown(entry.text)
        }
        is FeedEntry.Peer -> PultCard {
            val label = when (entry.direction) {
                PeerDirection.In -> "Из треда «${entry.peerTitle}»"
                PeerDirection.Out -> "В тред «${entry.peerTitle}»"
                null -> "Тред «${entry.peerTitle}»"
            }
            SmallLabel(label)
            UntrustedPlain(entry.text)
        }
        is FeedEntry.Notice -> UntrustedPlain(
            text = entry.text,
            style = PultTheme.typography.labelMedium,
            color = PultTheme.colors.onSurfaceVariant,
        )
        is FeedEntry.ToolCall -> ToolStepView(entry, result = null)
        is FeedEntry.ToolResult -> PultCard {
            ToolResultBody(entry)
        }
        is FeedEntry.Unrecognized -> SmallLabel("часть записей не распознана (${entry.count})")
    }
}

@Composable
fun ToolStepView(call: FeedEntry.ToolCall, result: FeedEntry.ToolResult?) {
    PultCard {
        UntrustedMono(text = "${call.tool}: ${call.summary}", maxLines = SUMMARY_LINES)
        if (call.input.isNotEmpty()) {
            UntrustedMono(
                text = call.input.joinToString("\n") { "${it.name}: ${it.value}" },
                color = PultTheme.colors.onSurfaceVariant,
                maxLines = INPUT_LINES,
            )
        }
        result?.let { ToolResultBody(it) }
    }
}

@Composable
private fun ToolResultBody(result: FeedEntry.ToolResult) {
    UntrustedMono(
        text = result.output,
        color = if (result.error) PultTheme.colors.error else PultTheme.colors.onSurfaceVariant,
        maxLines = OUTPUT_LINES,
    )
}

private const val SUMMARY_LINES = 4
private const val INPUT_LINES = 8
private const val OUTPUT_LINES = 12
