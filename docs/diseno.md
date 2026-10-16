# Lander — Documento de diseño

Oct 16, 2026 · @Drajana

## Resumen

Lander es una mascota de escritorio para Windows 11: un pequeño módulo lunar que vive sobre tus ventanas y la barra de tareas, con física real de aterrizaje. Por defecto vuela solo y tranquilo; con un atajo de teclado tomas el control y se convierte en un minijuego de Lunar Lander sobre tu propio escritorio.

**Pitch en una frase:** tu escritorio es la superficie de un planeta, y tus ventanas son las plataformas de aterrizaje.

**Enfoque elegido:** híbrido. Mascota tranquila la mayor parte del tiempo, juego arcade cuando tú quieres.

**Pilares de diseño**

1. **Vive en tu escritorio, no encima de él.** Las ventanas reales son el terreno; moverlas o cerrarlas tiene consecuencias.
2. **Nunca estorba.** No roba el foco, no bloquea clics y desaparece en pantalla completa.
3. **Física honesta.** Gravedad, inercia y combustible: aterrizar bien tiene mérito.
4. **Tiene personalidad.** En automático toma decisiones visibles, se cansa, se asusta y celebra.
5. **Ligero.** Casi sin consumo de CPU cuando está quieto.

## El mundo: tu escritorio

El juego lee la posición real de las ventanas de Windows 11 varias veces por segundo y convierte sus bordes en terreno.

| Superficie | Comportamiento |
| --- | --- |
| Barra de tareas | Suelo y base. Recarga combustible y repara daños. Es el punto de reaparición. |
| Barra de título de una ventana | Plataforma de aterrizaje. Si la ventana se mueve, el lander se mueve con ella (o resbala si va rápido). |
| Borde superior de la pantalla | Techo: rebote suave, sin daño. |
| Bordes laterales | Paredes. Con varios monitores, el lander puede cruzar al siguiente. |
| Ventana minimizada o cerrada bajo el lander | El suelo desaparece: cae con un pequeño sobresalto. |
| Ventana maximizada | Plataforma ancha justo bajo el borde superior, muy cómoda para descansar. |
| Escritorio vacío | Espacio abierto: se vuela libremente hasta la barra de tareas. |

**Eventos del mundo**

- **Terremoto:** si arrastras con fuerza la ventana donde está posado, se tambalea y puede volcar.
- **Viento:** al hacer scroll rápido en una ventana cercana, el lander nota una ráfaga lateral.
- **Noche:** con el modo oscuro de Windows activo, enciende sus luces de navegación.

## Modo manual

Con **Ctrl+Alt+L** tomas el control; vuelves a pulsarlo, o pasan 30 segundos sin tocar nada, y el lander regresa al modo automático. Mientras controlas, el teclado solo captura las teclas del juego, así que nunca escribes por accidente en otra ventana.

**Controles**

| Tecla | Acción |
| --- | --- |
| Flecha arriba | Motor principal (empuje) |
| Flechas izquierda / derecha | Propulsores laterales (giro o desplazamiento, según el modo de pilotaje) |
| Flecha abajo | Patas de aterrizaje y estabilizador |
| Espacio | Impulso corto de emergencia (gasta mucho combustible) |
| Esc | Soltar el control |

**Física**

- Gravedad constante hacia abajo, inercia y una leve resistencia del aire.
- Dos modos de pilotaje: **Clásico** (giras y empujas, como el Lunar Lander original) y **Asistido** (izquierda y derecha desplazan sin girar, más fácil).
- El empuje consume combustible; la barra de combustible aparece junto al lander solo en modo manual.

**Aterrizajes**

| Resultado | Condición | Efecto |
| --- | --- | --- |
| Perfecto | Velocidad baja, nave recta, patas sobre la plataforma | Puntos extra y pequeña celebración |
| Duro | Algo rápido o inclinado | Puntos normales, chispas, un poco de daño |
| Accidente | Demasiado rápido o de lado | Explosión pequeña y reaparición en la barra de tareas |

El daño se acumula y se repara descansando en la barra de tareas. Sin combustible, el lander planea despacio hasta el suelo: nunca se queda atascado.

## Modo automático

En automático el lander pasa la mayor parte del tiempo posado y, cada pocos minutos, elige un sitio nuevo y vuela hasta él. Sus decisiones se ven: antes de despegar mira hacia el destino y aparece un pequeño bocadillo con una flecha.

&#91;embedded content: modo automático · 7 estados\]

El ciclo normal es reposar, elegir destino, volar y aterrizar; dormir, esquivar y volver a base son las desviaciones que le dan vida.

**Personalidad**

- **Energía:** baja con cada vuelo y sube descansando. Con poca energía vuela más despacio y elige destinos cercanos.
- **Curiosidad:** sube cuando aparece una ventana nueva; le hace ir a "investigarla".
- **Timidez:** decide si huye del cursor o lo sigue. Se puede ajustar en la configuración.

**Reacciones al sistema**

| Evento de Windows | Reacción del lander |
| --- | --- |
| Se abre una ventana nueva | Gira la cabeza hacia ella y, a veces, va a posarse encima |
| Llega una notificación | Pequeño sobresalto y salto en el sitio |
| El PC lleva 5 minutos sin uso | Vuelve a la base y se duerme |
| Batería del portátil por debajo del 20 % | Vuelve a la base para "recargar" |
| Vacías la papelera | Va hasta la esquina de la papelera y hace una pequeña celebración |
| Cierras la ventana donde estaba | Cae, se recompone en el aire y busca otro sitio |

## Progresión, misiones y extras

La progresión es opcional y lenta: suma algo de motivación al modo manual sin convertir la mascota en una obligación.

**Puntos y nivel del piloto**

- Cada aterrizaje da puntos según su calidad y la dificultad de la plataforma (una ventana estrecha vale más que la barra de tareas).
- Rachas de aterrizajes perfectos multiplican los puntos.
- El nivel del piloto desbloquea mejoras y aspectos.

**Misiones cortas** (aparecen de vez en cuando como un pequeño aviso junto al lander)

- Aterriza en la ventana de una aplicación concreta ("posarse sobre el Explorador de archivos").
- Recoge tres cristales que aparecen flotando sobre el escritorio.
- Haz un viaje de ida y vuelta sin recargar combustible.
- Entrega un paquete de un monitor a otro.

**Mejoras desbloqueables**

- Depósito de combustible mayor, motor más eficiente, patas que absorben mejor los golpes.
- Aspectos: módulo Apolo retro, cápsula moderna, platillo volante, versión pixel art.
- Estelas de colores para el humo del motor.

**Ideas para más adelante**

- Varios landers a la vez, que se saludan o compiten por la misma plataforma.
- Modo carrera: un circuito de ventanas marcado con balizas y cronómetro.
- Logros sencillos con estadísticas (horas de vuelo, aterrizajes, accidentes).

## Integración con Windows 11 y reglas de convivencia

El objetivo es que el lander parezca parte de Windows 11 y que nunca interfiera con el trabajo.

**Reglas de convivencia**

- **Clics transparentes:** el ratón atraviesa toda la capa del juego salvo el propio lander.
- **Sin robar el foco:** la ventana del juego no aparece en Alt+Tab ni en la barra de tareas.
- **Pantalla completa:** si detecta un juego, vídeo o presentación a pantalla completa, aterriza y se oculta.
- **No molestar de Windows:** con las notificaciones silenciadas, el lander también se queda quieto y sin sonido.
- **Batería:** con el ahorro de energía activado baja la tasa de refresco o se duerme.

**Detalles propios de Windows 11**

- Barra de tareas centrada o alineada a la izquierda, en cualquier borde de la pantalla, y con ocultación automática (si se oculta, el lander se posa en el borde inferior).
- Esquinas redondeadas de las ventanas: el área de aterrizaje se ajusta para no colgar del vacío.
- Escritorios virtuales: el lander se queda en el escritorio activo.
- Snap Layouts: las ventanas encajadas forman "mesetas" contiguas por las que puede saltar.
- Escalado por monitor (DPI distinto en cada pantalla) y monitores con diferente altura.

**Icono en la bandeja del sistema**

- Pausar o reanudar, cambiar entre automático y manual, elegir aspecto.
- Ajustes: volumen, tamaño del lander, frecuencia de misiones, iniciar con Windows.
- Salir.

## Arquitectura técnica

Recomendación: **C# con .NET 8 y WPF**, porque da acceso directo a las APIs de Windows 11 que el juego necesita y genera un ejecutable pequeño. Godot 4 es la alternativa si prefieres un motor de juego con editor visual; también permite ventanas transparentes con clics pasantes.

&#91;embedded content: arquitectura · Windows 11 → módulos → física → render\]

Todo pasa por el módulo de física: tanto el control manual como la IA solo piden empuje, nunca mueven el lander directamente, así los dos modos se comportan igual.

**Piezas clave**

| Módulo | Cómo funciona |
| --- | --- |
| Capa de dibujo | Una ventana sin borde que cubre todos los monitores, siempre encima, con estilos WS\_EX\_LAYERED, WS\_EX\_TRANSPARENT y WS\_EX\_TOOLWINDOW (clics pasantes y fuera de Alt+Tab) |
| Terreno | Recorre las ventanas visibles en orden Z, toma su borde real con DwmGetWindowAttribute y descarta las ocultas, cubiertas o en otro escritorio virtual |
| Barra de tareas | Posición y ocultación automática con SHAppBarMessage |
| Teclado | Atajo global con RegisterHotKey; mientras controlas, se registran también las flechas y se liberan al soltar |
| Eventos | SetWinEventHook para ventanas nuevas, movidas o cerradas; avisos de energía y batería para el modo ahorro |
| Física | Paso fijo de 60 actualizaciones por segundo en vuelo y casi ninguna en reposo |
| Ajustes | Archivo JSON en la carpeta de datos del usuario; icono de bandeja con NotifyIcon |

## Arte y sonido

El lander mide unos 48 píxeles de alto por defecto (ajustable), con un estilo limpio que encaje con el diseño Fluent de Windows 11.

**Arte**

- Sprite vectorial o pixel art de alta resolución, con animaciones para: reposo, patas desplegándose, motor encendido, giro, dormido, celebración, explosión.
- Partículas para el humo, las chispas y el polvo al aterrizar sobre una ventana.
- Luces de navegación parpadeantes en modo oscuro.
- Bocadillos pequeños y discretos para mostrar destino o estado de ánimo, con iconos en lugar de texto.

**Sonido**

- Desactivado por defecto; se activa desde la bandeja.
- Zumbido suave del motor, clic metálico de las patas, pitido de aterrizaje perfecto.
- Volumen bajo y sonidos cortos: nada que distraiga en una videollamada.

## Hoja de ruta y riesgos

Cada fase deja algo jugable; la primera ya demuestra si la idea funciona.

1. **Prototipo:** capa transparente con clics que atraviesan, un rectángulo con gravedad que cae y se posa en la barra de tareas.
2. **Terreno real:** detección de ventanas, aterrizaje sobre barras de título, caída al cerrar o minimizar.
3. **Modo manual:** atajo global, controles, combustible, tipos de aterrizaje.
4. **Modo automático:** la máquina de estados con exploración, descanso y reacciones al ratón.
5. **Pulido:** sprites, partículas, sonido, icono en la bandeja, ajustes guardados.
6. **Extras:** misiones, progresión, aspectos, varios landers.
7. **Publicación:** instalador firmado (MSIX o similar) y, si interesa, Microsoft Store o itch.io.

**Riesgos**

| Riesgo | Mitigación |
| --- | --- |
| Ventanas con sombras o bordes invisibles dan posiciones falsas | Usar los límites reales que da el gestor de ventanas (DWM) en vez de los del rectángulo de la ventana |
| Varios monitores con distinto escalado | Trabajar en coordenadas físicas y declarar la app consciente del DPI por monitor |
| Consumo de CPU o batería | Bajar a pocas actualizaciones por segundo cuando está posado; pausar en pantalla completa |
| El antivirus desconfía del hook de teclado | Usar un atajo global registrado en vez de un hook; firmar el ejecutable |
| Ventanas de apps con permisos de administrador | Ignorarlas como terreno si no se pueden leer, sin dar errores |
| La mascota cansa con el tiempo | Comportamiento variado y tranquilo, sonido apagado por defecto, pausa con un clic |
