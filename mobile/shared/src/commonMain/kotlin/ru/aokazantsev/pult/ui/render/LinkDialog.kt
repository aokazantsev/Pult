package ru.aokazantsev.pult.ui.render

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultDialog
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.render.LinkTarget
import ru.aokazantsev.pult.render.TextSanitizer

@Composable
fun LinkDialog(link: LinkTarget, actions: LinkActions, onDismiss: () -> Unit) {
    PultDialog(
        title = "Ссылка из ленты",
        onDismiss = onDismiss,
        confirmText = if (link.openable) "Открыть" else "Копировать",
        onConfirm = {
            if (link.openable) actions.open(link.url) else actions.copy(link.url)
            onDismiss()
        },
        dismissText = if (link.openable) "Копировать" else "Закрыть",
        onDismissAction = {
            if (link.openable) actions.copy(link.url)
            onDismiss()
        },
    ) {
        Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
            PultText(text = TextSanitizer.oneLine(link.url), style = PultTheme.typography.code)
            if (!link.openable) {
                PultText(
                    text = "Эту ссылку можно только скопировать",
                    style = PultTheme.typography.bodySmall,
                    color = PultTheme.colors.onSurfaceVariant,
                )
            }
        }
    }
}
