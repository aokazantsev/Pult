package ru.aokazantsev.pult.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun MainScreen(viewModel: MainViewModel) {
    PultScaffold { padding ->
        Column(
            modifier = Modifier.fillMaxSize().padding(padding),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            PultText(text = "Pult", style = PultTheme.typography.displaySmall)
            PultText(
                text = viewModel.versionText,
                style = PultTheme.typography.bodyMedium,
                color = PultTheme.colors.onSurfaceVariant,
            )
        }
    }
}
