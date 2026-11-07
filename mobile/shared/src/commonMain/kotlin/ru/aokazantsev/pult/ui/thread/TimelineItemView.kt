package ru.aokazantsev.pult.ui.thread

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.session.TimelineItem
import ru.aokazantsev.pult.ui.render.UntrustedLine

@Composable
fun TimelineItemView(
    item: TimelineItem,
    draft: String?,
    onAllow: (String) -> Unit,
    onDeny: (String) -> Unit,
    onChoose: (String, List<ChoiceAnswer>) -> Unit,
    onSendText: (String, String) -> Unit,
) {
    when (item) {
        is TimelineItem.Entry -> FeedEntryView(item.entry)
        is TimelineItem.ToolStep -> ToolStepView(item.call, item.result)
        is TimelineItem.Unrecognized -> NoteView("часть записей не распознана (${item.count})")
        is TimelineItem.Gap -> NoteView("пропущены записи (${item.missing})")
        TimelineItem.EarlyMissing -> NoteView("ранние записи не показаны")
        TimelineItem.LateMissing -> NoteView("последние записи не загружены")
        is TimelineItem.Card -> RequestCardView(
            card = item.card,
            draft = draft,
            onAllow = { onAllow(item.card.requestId) },
            onDeny = { onDeny(item.card.requestId) },
            onChoose = { onChoose(item.card.requestId, it) },
            onSendText = { onSendText(item.card.requestId, it) },
        )
    }
}

@Composable
private fun NoteView(text: String) {
    Column(modifier = Modifier.fillMaxWidth()) {
        PultText(text = text, style = PultTheme.typography.labelMedium, color = PultTheme.colors.onSurfaceVariant)
    }
}

@Composable
fun SmallLabel(text: String) {
    UntrustedLine(text = text, style = PultTheme.typography.labelMedium, color = PultTheme.colors.onSurfaceVariant)
}
