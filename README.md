<p align="center">
  <img src="docs/images/portada.png" alt="Los cinco aspectos de Lander posados sobre una ventana y la barra de tareas" />
</p>

<h1 align="center">Lander</h1>

<p align="center">
  Una mascota de escritorio para Windows 11: un pequeño módulo lunar que vive sobre tus ventanas<br/>
  y la barra de tareas, con física real de aterrizaje.
</p>

<p align="center">
  <a href="https://github.com/Kobayashi82/Lander/actions/workflows/ci.yml"><img src="https://github.com/Kobayashi82/Lander/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/licencia-MIT-green" alt="Licencia MIT" /></a>
</p>

---

**Tu escritorio es la superficie de un planeta y tus ventanas son las plataformas de aterrizaje.**

Por defecto, el lander vuela solo y tranquilo: descansa en la barra de tareas o sobre la barra de título
de una ventana y, cada pocos minutos, elige otro sitio y vuela hasta él. Pulsa **Ctrl+Alt+L** y tomas
el control: se convierte en un minijuego de *Lunar Lander* sobre tu propio escritorio.

## Características

**El mundo es tu escritorio**
- Las barras de título de las ventanas son plataformas; la barra de tareas es el suelo y la base, donde
  reposta y se repara. Las ventanas que tapan a otras tapan también su plataforma.
- Si mueves la ventana donde está posado, se mueve con ella; si la mueves muy deprisa, resbala, y si la
  agitas, se tambalea y puede volcar. Si la cierras o la minimizas, se cae… y se recompone en el aire.
- Varios monitores con distinto tamaño, posición y escalado: puede cruzar de uno a otro.
- Hacer *scroll* rápido cerca de él levanta una ráfaga de viento. Con el modo oscuro de Windows enciende
  sus luces de navegación.
- **Experimental**: también puede posarse dentro de las ventanas, en bordes horizontales claros del
  contenido (separadores, cabeceras, tablas, el marco de una foto). Si haces *scroll*, se mueve con la
  página; si el borde sale de la vista, se cae y se recompone en el aire. Está desactivado por defecto
  (ver [Datos y privacidad](#datos-y-privacidad)).

**Modo automático con personalidad**
- Siete estados: reposar, elegir destino, volar, aterrizar, dormir, esquivar y volver a la base. Antes de
  despegar mira hacia el destino y muestra un bocadillo con una flecha.
- Energía, curiosidad y timidez: se cansa, investiga las ventanas nuevas y huye del cursor (o lo sigue,
  según lo tímido que lo configures).
- Reacciona a Windows: da un salto con las notificaciones, vuelve a la base a dormir si no usas el PC,
  va a recargar con la batería baja y celebra cuando vacías la papelera (vuela hasta su icono).
- Vuela de muchas formas y elige la que encaja con cada trayecto:
  - **Crucero**, **rasante** y **perezoso** (baja balanceándose como una hoja).
  - **En arco** y **ondulante**, subiendo y bajando como un delfín.
  - **Siguiendo el relieve**: pasa a ras de las ventanas que tiene debajo, subiendo y bajando con ellas.
  - **De precisión**: nivelado todo el vuelo, deslizándose con los propulsores de traslación como al
    atracar una nave. Lo prefiere para sitios delicados.
  - **Salto balístico**: un empujón inclinado, la parábola con el motor apagado (a veces con voltereta) y
    una frenada final inclinado contra el movimiento, encendiendo en el último instante posible.
  - **En picado**: baja en diagonal muy deprisa y se encabrita para frenar de golpe.
  - **Caída libre con frenada final** (*suicide burn*): sube sobre el destino, apaga el motor, cae y
    frena en el último instante.
  - **A saltitos** por la barra de tareas o por una barra de título, posándose entre salto y salto.
  - **Vuelos panorámicos** por todo el monitor y, de vez en cuando, una voltereta.
- Antes de posarse en un sitio nuevo, a veces se queda un momento encima mirando a un lado y a otro. Si
  pasa volando junto al cursor, te saluda balanceando la nave; si lo asustas, sale disparado con el impulso.
- Cuántas acrobacias hace (volteretas, caídas libres, saltos balísticos, picados) se ajusta en
  **Ajustes → Comportamiento → Acrobacias**; sin acrobacias vuela siempre con calma.
- Calcula el combustible de cada vuelo: no sale si no le llega para volver a la base, se desvía o
  aterriza antes de quedarse seco, y nunca choca con el borde superior de la pantalla. Con varios
  monitores de distinto tamaño comprueba que el recorrido no pase por fuera de las pantallas.
- Animaciones cuidadas: se agacha antes de despegar y se estira al saltar, suelta una bocanada de humo al
  apagar el motor y un fogonazo al encenderlo a fondo, de noche enciende un faro al aterrizar y, al
  reaparecer tras un accidente, llega en una columna de luz.
- En reposo pasea por la barra de título, se asoma al borde y se tira, bosteza, se estira, echa siestas,
  te saluda al volver, se deja acariciar con el cursor, acompaña a la ventana que activas y visita el
  reloj de la barra de tareas a las horas en punto.
- Despistes opcionales: a veces calcula mal la frenada o llega torcido (aterrizaje duro y, muy de vez en
  cuando, un accidente). Se ajusta en **Ajustes → Comportamiento**.

**Modo manual**
- Física honesta: gravedad, inercia, resistencia y combustible.
- Un único esquema de control como una nave de verdad: motor principal, propulsores de giro (← →) y de
  traslación (A D), y patas que se sacan y recogen (G). Todos los propulsores se ven encenderse.
- Aterrizajes perfectos, duros o accidentes; puntos según la dificultad de la plataforma, rachas,
  niveles de piloto, mejoras y aspectos desbloqueables. Los bordes del contenido de las ventanas, si los
  activas, puntúan como una ventana estrecha.
- Misiones cortas: posarse sobre una aplicación concreta, recoger cristales, ida y vuelta sin repostar o
  llevar un paquete a otro monitor.

**Modo carrera**
- Un circuito contrarreloj sobre tus propias ventanas: balizas numeradas sobre sus barras de título que
  hay que visitar en orden antes de volver a la base. El cronómetro arranca al despegar.
- Cada baliza rellena el depósito; un aterrizaje brusco suma 2 segundos y un accidente te deja fuera.
- Medalla de oro, plata o bronce según un tiempo de referencia calculado a partir del recorrido. Puedes
  repetir el mismo circuito para batir tu récord.
- Desde el menú del lander o de la bandeja (**Empezar una carrera**) o en **Ajustes → General**.

**Logros**
- Catorce logros sencillos a partir de tus estadísticas: rachas, cristales, misiones, medallas, horas de
  vuelo… Se ven con su progreso en **Ajustes → Piloto**.

**Nunca estorba**
- El ratón atraviesa toda la capa del juego salvo el propio lander. No roba el foco ni aparece en
  Alt+Tab o en la barra de tareas.
- Con un vídeo, un juego o una presentación a pantalla completa se muda a otro monitor libre o se
  esconde. Con «No molestar», se queda quieto y en silencio.
- Dibujado por GPU (Direct2D + DirectComposition) al ritmo del monitor, con animaciones suaves a 60,
  144 Hz o lo que dé tu pantalla. En reposo consume en torno al **0,05 % de un núcleo** de CPU.
- Sonido desactivado por defecto; sonidos cortos y a volumen bajo.

<p align="center">
  <img src="docs/images/ajustes.png" alt="Ventana de ajustes con el estilo de Windows 11" width="720" />
</p>

## Controles

| Tecla | Acción |
| --- | --- |
| **Ctrl+Alt+L** | Tomar o devolver el control (configurable) |
| ↑ | Motor principal (empuja hacia donde apunta la nave) |
| ← → | Girar con los propulsores de giro; al soltar mantienen la inclinación |
| A D | Desplazarse a un lado con los propulsores de traslación, sin girar |
| ↓ | Enderezar la nave |
| G | Sacar o recoger las patas |
| Espacio | Impulso de emergencia (gasta mucho combustible) |
| Esc | Soltar el control |

Todos los propulsores se ven encenderse. Sin combustible no hay motor: el lander cae.

Mientras pilotas, solo se capturan esas teclas: nunca escribes por accidente en otra ventana. Si pasan
30 segundos sin tocar nada, el lander vuelve a volar solo.

Con el ratón: **arrástralo** para cogerlo y lanzarlo, **haz clic** para hacerle una caricia, **doble
clic** para pilotarlo y **clic derecho** para abrir el menú. Un clic en el icono de la bandeja lo pone en
pausa.

## Instalación

Descarga la última versión desde [Releases](https://github.com/Kobayashi82/Lander/releases):

- `Lander-x.y.z-win-x64.zip`: ejecutable de 1,3 MB. Necesita el
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `Lander-x.y.z-win-x64-standalone.zip`: no necesita instalar nada (≈65 MB).

Si no sabes cuál elegir, descarga el autónomo. En ambos casos es un único `Lander.exe`, sin instalador:
descomprímelo donde quieras y ejecútalo. El lander aparecerá en la barra de tareas y su icono en el área
de notificación. Para que arranque con Windows, actívalo en **Ajustes → General**.

El ejecutable no está firmado, así que la primera vez Windows SmartScreen puede avisar de que es una
aplicación desconocida: pulsa **Más información → Ejecutar de todas formas**.

Requisitos: Windows 10 u 11 (x64). Está pensado para Windows 11.

## Compilar desde el código

Necesitas el [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build                                   # compilar todo
dotnet test --project tests/Lander.Core.Tests  # tests del núcleo
dotnet run --project src/Lander.App            # ejecutar

# Publicar
dotnet publish src/Lander.App -p:PublishProfile=win-x64             # ejecutable pequeño
dotnet publish src/Lander.App -p:PublishProfile=win-x64-standalone  # autónomo
```

Los ejecutables quedan en `artifacts/publish/`. Al subir una etiqueta `v*` (por ejemplo `v0.1.0`),
GitHub Actions compila, prueba y publica una versión con ambos ejecutables.

### Opciones de línea de órdenes

| Opción | Para qué sirve |
| --- | --- |
| `--settings` | Abre los ajustes al arrancar. |
| `--exit` | Cierra limpiamente la instancia en marcha. |
| `--demo` | Modo demostración: simula a alguien delante del PC y vuela mucho más a menudo. No guarda el progreso. |
| `--demo-manual` | Como `--demo`, pero pilotando en modo manual con un piloto simulado (sin capturar el teclado). |
| `--demo-race` | Como `--demo-manual`, pero corriendo una carrera tras otra (necesita al menos dos ventanas a la vista). |
| `--demo-flights` | Como `--demo`, pero cada vuelo usa la siguiente técnica (salto balístico, picado, saltitos, precisión, relieve, ondulante, caída libre...) y con las acrobacias al máximo. |
| `--software` | Dibuja por CPU en lugar de por GPU (para comparar o si un controlador da problemas). |
| `--show-in-captures` | Con «Posarse en el contenido» activado, el lander sigue saliendo en las capturas y grabaciones de pantalla (al analizar la pantalla se ignora la zona donde está). |
| `--stats` | Registra cada diez segundos ciclos, fotogramas y tiempos de dibujo, y el motivo y la técnica de cada vuelo. |
| `--preview archivo.png` | Dibuja todos los aspectos, expresiones, bocadillos y el HUD en una imagen. |
| `--preview-hero archivo.png` | Genera la imagen de portada de este README. |
| `--preview-settings carpeta` | Guarda cada página de los ajustes como imagen, sin mostrarlos. |
| `--export-icon archivo.ico` | Regenera el icono del programa a partir del dibujo del lander. |

## Estructura

```
src/
  Lander.Core/    Simulación pura y testeable, sin dependencias de Windows:
                  terreno, física, piloto automático, misiones, carreras, progresión,
                  logros y ajustes.
  Lander.App/     Aplicación WPF: integración con Windows, motor, render, sonido,
                  bandeja del sistema y ventana de ajustes.
tests/
  Lander.Core.Tests/  Tests unitarios y simulaciones completas del piloto automático.
docs/
  diseno.md           Documento de diseño original.
  arquitectura.md     Decisiones técnicas y cómo encaja todo.
```

La arquitectura se explica en [docs/arquitectura.md](docs/arquitectura.md) y el diseño del juego en
[docs/diseno.md](docs/diseno.md).

## Datos y privacidad

Lander no se conecta a internet. Guarda los ajustes y el progreso en `%APPDATA%\Lander` y un registro
de los últimos siete días en `%LOCALAPPDATA%\Lander\logs`. Para leer el escritorio consulta la posición
de las ventanas y el nombre de su aplicación.

Solo si activas **Posarse en el contenido (experimental)** (en **Ajustes → Comportamiento** o en el menú
de la bandeja; viene desactivado) mira además lo que hay dentro de las ventanas:

- Toma imágenes de la pantalla con la duplicación del escritorio de Windows, solo del monitor donde está
  el lander y solo cuando hace falta: unos segundos antes de elegir destino, mientras está posado o vuela
  hacia el contenido de una ventana y mientras lo pilotas tú. Como mucho dos veces por segundo por ventana
  (más a menudo solo para seguir el *scroll* de la ventana en la que está posado) y nunca si esa parte de
  la pantalla no ha cambiado. En reposo no mira nada.
- Las imágenes se pasan a escala de grises y se analizan en memoria para encontrar bordes; después se
  descartan. **Nunca se guardan en disco ni se envían a ningún sitio**, y no se reconoce texto ni nada de
  lo que hay en ellas: solo dónde hay líneas horizontales.
- Nunca mira las ventanas de gestores de contraseñas (KeePass, Bitwarden, 1Password, LastPass…), las
  pantallas de credenciales de Windows, ni ventanas cuyo título indique banca, pagos, contraseñas, un
  inicio de sesión o navegación privada. Puedes añadir más aplicaciones en **Aplicaciones excluidas**.
- El contenido protegido (vídeo con DRM) sale negro en la captura y simplemente no da bordes.
- Para no verse a sí mismo, mientras la opción está activada las ventanas del lander se excluyen de las
  capturas de pantalla, así que **el lander no sale en capturas ni grabaciones** (salvo con
  `--show-in-captures`).

## Estado

Implementadas las fases 1 a 6 del diseño (prototipo, terreno real, modo manual, modo automático,
pulido y extras), el modo carrera, los logros, la publicación automática de ejecutables y, como opción
experimental, posarse en el contenido de las ventanas. Ideas pendientes:

- Varios landers a la vez.
- Instalador (MSIX) y publicación en Microsoft Store o itch.io.

## Cambios

Las novedades de cada versión están en [CHANGELOG.md](CHANGELOG.md).

## Licencia

[MIT](LICENSE).
