package ru.aokazantsev.pult.designsystem.component

import androidx.compose.foundation.layout.size
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.LocalContentColor
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun PultIcon(
    icon: ImageVector,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    tint: Color = LocalContentColor.current,
) {
    Icon(
        imageVector = icon,
        contentDescription = contentDescription,
        modifier = modifier.size(PultTheme.sizes.icon),
        tint = tint,
    )
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun PultIconButton(
    icon: ImageVector,
    contentDescription: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    stateDescription: String? = null,
) {
    val described = if (stateDescription == null) modifier else modifier.semantics { this.stateDescription = stateDescription }
    IconButton(onClick = onClick, shapes = IconButtonDefaults.shapes(), modifier = described) {
        PultIcon(icon = icon, contentDescription = contentDescription)
    }
}
