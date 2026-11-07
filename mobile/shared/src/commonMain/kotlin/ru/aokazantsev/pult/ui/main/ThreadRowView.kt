package ru.aokazantsev.pult.ui.main

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.session.ThreadRow
import ru.aokazantsev.pult.ui.Labels
import ru.aokazantsev.pult.ui.render.UntrustedLine

@Composable
fun ThreadRowView(row: ThreadRow, onClick: () -> Unit) {
    val info = row.info
    Column(modifier = Modifier.fillMaxWidth().padding(start = INDENT.dp * row.depth)) {
        PultCard(modifier = Modifier.clickable(onClick = onClick)) {
            info.forkOf?.let { fork ->
                UntrustedLine(
                    text = "форк «${fork.title}»",
                    style = PultTheme.typography.labelMedium,
                    color = PultTheme.colors.onSurfaceVariant,
                )
            }
            UntrustedLine(text = info.title, style = PultTheme.typography.titleMedium, maxLines = 2)
            val details = listOf(info.folder.takeIf { it.isNotEmpty() }, Labels.status(info)).filterNotNull().joinToString(" · ")
            UntrustedLine(text = details, style = PultTheme.typography.bodySmall, color = PultTheme.colors.onSurfaceVariant)
            Labels.marks(row.marks).forEach { mark ->
                UntrustedLine(text = mark, style = PultTheme.typography.labelLarge, color = PultTheme.colors.primary)
            }
            if (info.pcDialog) {
                UntrustedLine(text = Labels.PC_DIALOG, style = PultTheme.typography.labelMedium, color = PultTheme.colors.tertiary)
            }
            Labels.turnNote(info.turnNote)?.let { note ->
                UntrustedLine(text = note, style = PultTheme.typography.labelMedium, color = PultTheme.colors.error, maxLines = 3)
            }
        }
    }
}

private const val INDENT = 20
