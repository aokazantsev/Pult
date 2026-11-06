package ru.aokazantsev.pult.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.LoadingIndicator
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import ru.aokazantsev.pult.designsystem.icon.PultIcons
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun PultLoading(modifier: Modifier = Modifier, text: String? = null) {
    StateColumn(modifier) {
        LoadingIndicator(modifier = Modifier.size(PultTheme.sizes.loader))
        if (text != null) {
            PultText(text = text, color = PultTheme.colors.onSurfaceVariant, textAlign = TextAlign.Center)
        }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun PultLoadingIndicator(modifier: Modifier = Modifier) {
    Box(modifier = modifier.padding(PultTheme.spacing.xl), contentAlignment = Alignment.Center) {
        LoadingIndicator(modifier = Modifier.size(PultTheme.sizes.loader))
    }
}

@Composable
fun PultError(
    message: String,
    modifier: Modifier = Modifier,
    actionText: String? = null,
    onAction: () -> Unit = {},
    extraActions: @Composable () -> Unit = {},
) {
    StateColumn(modifier) {
        PultIcon(
            icon = PultIcons.Error,
            contentDescription = null,
            tint = PultTheme.colors.error,
            modifier = Modifier.size(PultTheme.sizes.iconLarge),
        )
        PultText(text = message, textAlign = TextAlign.Center)
        if (actionText != null) {
            PultSecondaryButton(text = actionText, onClick = onAction)
        }
        extraActions()
    }
}

@Composable
private fun StateColumn(modifier: Modifier, content: @Composable () -> Unit) {
    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .padding(PultTheme.spacing.xl),
            verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.l),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            content()
        }
    }
}
