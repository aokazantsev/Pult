package ru.aokazantsev.pult.ui.access

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.foundation.layout.padding
import ru.aokazantsev.pult.designsystem.component.PultError
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultTopBar

@Composable
fun LockScreen(onUnlock: () -> Unit) {
    PultScaffold(topBar = { PultTopBar(title = "Pult") }) { padding ->
        PultError(
            message = "Подтвердите вход: биометрия или PIN устройства",
            actionText = "Войти",
            onAction = onUnlock,
            modifier = Modifier.padding(padding),
        )
    }
}

@Composable
fun NoSecureLockScreen() {
    PultScaffold(topBar = { PultTopBar(title = "Pult") }) { padding ->
        PultError(
            message = "Включите блокировку экрана — без неё Pult не защищён",
            modifier = Modifier.padding(padding),
        )
    }
}
