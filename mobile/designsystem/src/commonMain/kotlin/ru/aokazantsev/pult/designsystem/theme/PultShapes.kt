package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.MaterialShapes
import androidx.compose.material3.Shapes
import androidx.compose.material3.toShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Shape

@Immutable
class PultShapes(
    val extraSmall: Shape,
    val small: Shape,
    val medium: Shape,
    val large: Shape,
    val extraLarge: Shape,
    val full: Shape,
    val cookie: Shape,
    val clover: Shape,
    val sunny: Shape,
    val softBurst: Shape,
) {
    companion object {
        @OptIn(ExperimentalMaterial3ExpressiveApi::class)
        @Composable
        internal fun default(): PultShapes {
            val scale = remember { Shapes() }
            val cookie = MaterialShapes.Cookie9Sided.toShape()
            val clover = MaterialShapes.Clover4Leaf.toShape()
            val sunny = MaterialShapes.Sunny.toShape()
            val softBurst = MaterialShapes.SoftBurst.toShape()
            return remember(scale, cookie, clover, sunny, softBurst) {
                PultShapes(
                    extraSmall = scale.extraSmall,
                    small = scale.small,
                    medium = scale.medium,
                    large = scale.large,
                    extraLarge = scale.extraLarge,
                    full = CircleShape,
                    cookie = cookie,
                    clover = clover,
                    sunny = sunny,
                    softBurst = softBurst,
                )
            }
        }
    }
}
