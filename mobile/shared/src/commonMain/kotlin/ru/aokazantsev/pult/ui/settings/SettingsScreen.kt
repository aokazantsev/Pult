package ru.aokazantsev.pult.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultSecondaryButton
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.ui.main.SOFTWARE_KEYS_WARNING

@Composable
fun SettingsScreen(viewModel: SettingsViewModel, onDone: () -> Unit) {
    val state by viewModel.state.collectAsState()
    val info = state.pairInfo
    PultScaffold(topBar = { PultTopBar(title = "Настройка", onBack = onDone) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(PultTheme.spacing.l)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
        ) {
            PultCard {
                if (info == null) {
                    PultText(text = "Телефон не сопряжён с ПК")
                } else {
                    PultText(text = "Сервер", style = PultTheme.typography.labelMedium, color = PultTheme.colors.onSurfaceVariant)
                    PultText(text = info.server)
                    PultText(text = "Отпечаток пары", style = PultTheme.typography.labelMedium, color = PultTheme.colors.onSurfaceVariant)
                    PultText(text = info.fingerprint, style = PultTheme.typography.titleMedium)
                    PultText(text = "Простой до разрыва, часов: ${info.idleSeconds / SECONDS_IN_HOUR}")
                    if (info.softwareKeys) PultText(text = SOFTWARE_KEYS_WARNING, color = PultTheme.colors.error)
                }
                PultText(text = viewModel.versionText, color = PultTheme.colors.onSurfaceVariant)
            }
            if (info != null) {
                PultSecondaryButton(
                    text = "Отвязать",
                    onClick = {
                        viewModel.unpair()
                        onDone()
                    },
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}

private const val SECONDS_IN_HOUR = 3600
