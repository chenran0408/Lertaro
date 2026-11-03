# Historial del portapapeles

El complemento de historial del portapapeles registra lo que copias en este equipo y te permite buscarlo y volver a pegarlo. Todo permanece local.

## Abrir el panel

Pulsa `Win+V` (configurable en Ajustes → Atajos) o escribe la palabra clave `cb` en el cuadro de búsqueda y elige **Abrir el panel del portapapeles**. El panel se abre en la posición del ratón.

Si `Win+V` no hace nada, el historial del portapapeles de Windows aún posee esa tecla: abre **Ajustes → Complementos → Historial del portapapeles → Configurar** y usa **Abrir la configuración del portapapeles de Windows** para desactivar la función del sistema.

## Qué se registra

- El **texto** que copias, hasta la longitud configurada.
- Las **imágenes** que copias (capturas, imágenes web), mostradas como miniaturas.
- Los **archivos y carpetas** que copias en el Explorador.

Las copias hechas con un gestor de contraseñas en primer plano se omiten. La lista de exclusión predeterminada cubre Keepass, KeepassXC, 1Password, Bitwarden y Enpass, y puedes ampliarla en los ajustes.

## Uso del panel

- Escribe para filtrar la lista; la coincidencia usa los mismos niveles difuso y pinyin que la búsqueda principal.
- Las pestañas superiores filtran por tipo; la de favoritos muestra solo los fijados.
- La lista se agrupa: fijados primero, luego hoy, ayer y fechas anteriores.
- `Entrar` pega la entrada seleccionada en la ventana que estabas usando; `Ctrl+Entrar` la pega como texto sin formato; `1`–`9` pegan las primeras nueve filas; `Supr` elimina; `Ctrl+P` fija o desfija; `Esc` cierra el panel.
- Selecciona varias filas con `Ctrl`+clic o `Mayús`+clic para pegarlas combinadas: los textos se unen con saltos de línea y las rutas de archivo se combinan sin duplicados.
- Las entradas fijadas mantienen el panel abierto tras pegar, así un texto que guardes a mano puede pegarse repetidamente.

## Ajustes

- **Registrar el historial del portapapeles**: activa o desactiva la captura; el historial existente se conserva.
- **Conservar el historial durante**: 1, 7 o 30 días, o para siempre. Las entradas antiguas sin fijar se eliminan automáticamente; las fijadas nunca caducan.
- **Número máximo de entradas**: el límite de entradas sin fijar.
- **Longitud máxima del texto**: cuánto texto se guarda por entrada.
- **Aplicaciones excluidas**: nombres de proceso, separados por comas.

El historial se guarda localmente en tu directorio de datos de usuario. Vaciar el historial del panel también borra de forma definitiva las imágenes guardadas.
