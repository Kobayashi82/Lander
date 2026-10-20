# Cambios

Todas las novedades de Lander, de la más reciente a la más antigua.

## 1.0.0

Primera versión publicada: el ejecutable autónomo `Lander.exe` en Releases.

## 0.4.0

**Técnicas de vuelo nuevas en el modo automático**
- **Salto balístico**: un empujón inclinado, la parábola con el motor apagado (a veces con voltereta en el
  aire) y una frenada final inclinado contra el movimiento, encendiendo en el último instante posible.
- **En picado**: baja en diagonal muy deprisa y se encabrita para frenar de golpe.
- **A saltitos** por la barra de tareas o por una barra de título, posándose entre salto y salto.
- **Siguiendo el relieve**: pasa a ras de las ventanas que tiene debajo, subiendo y bajando con ellas.
- **De precisión**: nivelado todo el vuelo, deslizándose con los propulsores de traslación.
- **Ondulante**, subiendo y bajando como un delfín.
- Antes de posarse en un sitio nuevo, a veces se queda un momento encima mirando a un lado y a otro; si
  pasa volando junto al cursor, saluda; si se asusta, sale disparado con el impulso.
- Nuevo ajuste **Acrobacias** (ninguna, pocas, normales, muchas) en **Ajustes → Comportamiento** y en el
  menú de la bandeja.

**Animaciones**
- Se agacha antes de despegar y se estira al saltar.
- Bocanada de humo al apagar el motor y fogonazo al encenderlo a fondo.
- De noche enciende un faro al aterrizar.
- Al reaparecer tras un accidente llega en una columna de luz.
- Sonido de aire en las volteretas.
- Los bocadillos del reloj (horas en punto) y del saludo salían vacíos: ahora tienen su icono (un
  despertador que suena y una mano que saluda).

**Más seguro y más robusto**
- Con monitores de distinto tamaño o altura comprueba que el recorrido no pase por fuera de las
  pantallas, y el control del techo tiene en cuenta el borde del monitor hacia el que se desplaza.
- Frena antes ante las paredes (cuenta con lo que tarda en girar) y se ayuda de los propulsores de
  traslación.
- Corregidos dos casos en los que podía quedarse sin combustible: con una ventana pegada al borde
  superior, el arco apuntaba por encima de la pantalla y, al recuperarse, podía elegir una plataforma
  inalcanzable y quedarse en el aire.
- Un error inesperado en un ciclo ya no detiene el motor: se registra y el lander sigue; si falla la
  captura del contenido de las ventanas (experimental), solo se desactiva esa función.
- `--demo-flights` enseña todas las técnicas de vuelo una tras otra, y `--stats` registra el motivo y la
  técnica de cada vuelo.

## 0.3.0 y anteriores

- Dibujo por GPU (Direct2D + DirectComposition) al ritmo del monitor, con `--software` como alternativa.
- Piloto automático nuevo: rutas variadas (crucero, rasante, perezoso, arco, vuelos panorámicos), frenada
  en el último instante, cálculo del combustible y nunca contra el borde superior. Despistes opcionales.
- Un único esquema de control manual (giro, traslación y patas).
- **Experimental**: posarse en bordes del contenido de las ventanas y seguirlos al hacer scroll.
- Modo carrera con medallas y récords por circuito, catorce logros y licencia MIT.
- Modo manual con puntuación justa, misiones cortas, niveles y aspectos desbloqueables.
- Las bases: el lander vive sobre las ventanas y la barra de tareas, con física real, bandeja del
  sistema, ajustes al estilo de Windows 11, escritorios virtuales y publicación automática.
