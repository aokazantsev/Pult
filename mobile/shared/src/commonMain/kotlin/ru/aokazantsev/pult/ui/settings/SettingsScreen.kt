package ru.aokazantsev.pult.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import ru.aokazantsev.pult.designsystem.component.PultPrimaryButton
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultTextField
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun SettingsScreen(viewModel: SettingsViewModel, onDone: () -> Unit) {
    var serverUrl by remember { mutableStateOf(viewModel.current.serverUrl) }
    var pairId by remember { mutableStateOf(viewModel.current.pairId) }
    var token by remember { mutableStateOf(viewModel.current.token) }
    PultScaffold(topBar = { PultTopBar(title = "Настройка пары", onBack = onDone) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(PultTheme.spacing.l)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
        ) {
            PultTextField(
                value = serverUrl,
                onValueChange = { serverUrl = it },
                label = "Адрес сервера",
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.fillMaxWidth(),
            )
            PultTextField(
                value = pairId,
                onValueChange = { pairId = it },
                label = "Пара",
                modifier = Modifier.fillMaxWidth(),
            )
            PultTextField(
                value = token,
                onValueChange = { token = it },
                label = "Токен телефона",
                modifier = Modifier.fillMaxWidth(),
            )
            PultPrimaryButton(
                text = "Сохранить",
                enabled = serverUrl.isNotBlank() && pairId.isNotBlank() && token.isNotBlank(),
                onClick = {
                    viewModel.save(serverUrl, pairId, token)
                    onDone()
                },
            )
        }
    }
}
