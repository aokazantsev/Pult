package ru.aokazantsev.pult.designsystem.component

import androidx.compose.material3.AlertDialog
import androidx.compose.runtime.Composable
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun PultDialog(
    title: String,
    onDismiss: () -> Unit,
    confirmText: String,
    onConfirm: () -> Unit,
    dismissText: String? = null,
    onDismissAction: () -> Unit = onDismiss,
    content: @Composable () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { PultText(text = title, style = PultTheme.typography.titleLarge) },
        text = content,
        confirmButton = { PultTextButton(text = confirmText, onClick = onConfirm) },
        dismissButton = dismissText?.let { text -> { PultTextButton(text = text, onClick = onDismissAction) } },
    )
}
