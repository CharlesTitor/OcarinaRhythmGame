using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;


/*
 MPR121SerialBridge

Recibe los mensajes enviados por Arduino:

  T,0 = toque nuevo en el carril 0
  R,0 = se soltó el botón del carril 0
  T,1 = toque nuevo en el carril 1
  R,1 = se soltó el botón del carril 1

Arduino envía el número de la posición del botón en su lista.
Por eso, si en Arduino hay cuatro botones, Unity recibe los carriles 0, 1, 2 y 3.

Este script solo recibe y organiza las pulsaciones.
Otro componente del juego consultará los métodos FuePresionadoEsteFrame()
y EstaPresionado().
*/
public class MPR121SerialBridge : MonoBehaviour
{
  
    // CONFIGURACIÓN VISIBLE EN EL INSPECTOR
  

    [Header("Conexión con Arduino")]

    [Tooltip("Puerto COM de Arduino, por ejemplo COM3 o COM6.")]
    public string nombrePuerto = "COM3";

    [Tooltip("Debe coincidir con Serial.begin() en el programa de Arduino.")]
    public int velocidad = 115200;

    [Header("Botones del juego")]

    [Tooltip("Cantidad de botones configurados en Arduino. " +
        "Si la lista de Arduino tiene cuatro entradas, escribe 4.")]
    [Range(1, 12)]
    public int cantidadCarriles = 4;
    // OBJETOS PARA ABRIR Y LEER EL PUERTO SERIAL
 

    // SerialPort representa una conexión serial de la computadora.
    // A través de este objeto Unity abre, lee y cierra el puerto COM.
    private SerialPort puerto;

    // Thread representa un hilo de ejecución independiente.
    // En este caso, ese hilo espera mensajes del Arduino mientras Unity
    // continúa ejecutando el juego.
    private Thread hiloLectura;

    // Esta variable le indica al hilo lector si debe seguir funcionando.
    // "volatile" permite que el hilo lector y el hilo principal de Unity
    // vean los cambios de esta variable.
    private volatile bool leyendo = false;

    // ConcurrentQueue es una cola segura para compartir datos entre hilos.
    // El hilo lector agrega mensajes y Update() los retira para procesarlos.
    private readonly ConcurrentQueue<string> colaMensajes =
        new ConcurrentQueue<string>();


    // ESTADOS DE LOS BOTONES


    // Guarda qué botones recibieron un toque nuevo durante el frame actual.
    // Se limpia al comienzo de cada Update().
    private readonly bool[] presionadoEsteFrame = new bool[12];

    // Guarda qué botones siguen físicamente tocados.
    // El mensaje T activa el estado y el mensaje R lo desactiva.
    private readonly bool[] siguePresionado = new bool[12];

    // Indica si el puerto COM se abrió correctamente.
    public bool Conectado { get; private set; }
    private void Start()
    {
        AbrirPuerto();
    }
    
    // AbrirPuerto()
    // Crea y abre la conexión serial. Si la apertura funciona, inicia el hilo
    // que estará leyendo mensajes del Arduino.
 
    private void AbrirPuerto()
    {
        try
        {
            // El constructor SerialPort(nombrePuerto, velocidad) crea el objeto
            // serial con el COM y los baudios indicados en el Inspector.
            puerto = new SerialPort(nombrePuerto, velocidad);

            // NewLine indica qué carácter marca el final de una línea.
            // Arduino usa Serial.println(), que termina cada mensaje con "\n".
            puerto.NewLine = "\n";

            // ReadTimeout configura el tiempo máximo que ReadLine() esperará
            // por una línea completa. El valor está expresado en milisegundos.
            puerto.ReadTimeout = 100;

            // Open() solicita al sistema operativo abrir el puerto COM.
            // Si otro programa lo tiene abierto, esta llamada puede generar
            // una excepción que será atendida por el catch.
            puerto.Open();

            Conectado = true;
            leyendo = true;

            // new Thread(...) crea el hilo lector y le indica qué método ejecutar.
            hiloLectura = new Thread(LeerPuertoEnSegundoPlano);

            // IsBackground indica que este hilo es de fondo.
            // Así no mantiene la aplicación abierta por sí solo al cerrar Unity.
            hiloLectura.IsBackground = true;

            // Start() inicia la ejecución del hilo y comienza a llamar al método
            // LeerPuertoEnSegundoPlano().
            hiloLectura.Start();

            Debug.Log("Puerto abierto: " + nombrePuerto + " a " + velocidad + " baudios.");
        }
        catch (Exception error)
        {
            // Exception reúne errores posibles, por ejemplo: COM incorrecto,
            // puerto ocupado o problema al abrir la conexión.
            Conectado = false;
            leyendo = false;

            // Debug.LogError escribe un mensaje rojo en la consola de Unity.
            Debug.LogError( "No se pudo abrir " + nombrePuerto +
                ". Detalle: " + error.Message);
        }
    }

    /*
     LeerPuertoEnSegundoPlano()
    
     Se ejecuta en hiloLectura, separado del hilo principal de Unity.
     Su trabajo es esperar líneas del Arduino y guardarlas en colaMensajes.
    
     No se modifican objetos del juego desde aquí; Update() procesará los
     mensajes desde el hilo principal.
    */
    private void LeerPuertoEnSegundoPlano()
    {
        // El while repite la lectura mientras leyendo sea true.
        while (leyendo)
        {
            try
            {
                // ReadLine() espera hasta recibir el carácter NewLine configurado
                // arriba. Por ejemplo, recibe "T,0\n" y devuelve el texto "T,0".
                // La espera ocurre en el hilo secundario y no detiene el juego.
                string mensaje = puerto.ReadLine();

                // Trim() elimina espacios y caracteres en los extremos del texto.
                // Arduino puede terminar la línea con "\r\n"; Trim elimina el "\r"
                // que queda después de que ReadLine() retira "\n".
                mensaje = mensaje.Trim();

                // IsNullOrEmpty() comprueba si el texto es null o está vacío.
                // Solo se agregan mensajes que contengan algún carácter.
                if (!string.IsNullOrEmpty(mensaje))
                {
                    // Enqueue() agrega el mensaje al final de colaMensajes.
                    // Update() lo retirará y procesará después.
                    colaMensajes.Enqueue(mensaje);
                }
            }
            catch (TimeoutException)
            {
                // TimeoutException se produce si ReadLine() no recibe una línea
                // completa en 100 ms. No significa necesariamente que algo falló:
                // puede que el jugador simplemente no haya tocado un botón.
            }
            catch (Exception error)
            {
                // Otros errores pueden indicar que se desconectó el cable o se
                // cerró el puerto mientras se estaba leyendo
                // Encola el error para que Update() lo muestre en la consola.
                colaMensajes.Enqueue("SYS_ERROR," + error.Message);

                // Cambia la bandera para pedirle al while que termine.
                leyendo = false;
            }
        }
    }   
    // Unity llama a Update() una vez por cada frame del juego.
    // Aquí se limpian los toques del frame anterior y se procesan los mensajes
    // que hayan llegado desde Arduino.

    private void Update()
    {
        // Array.Clear() llena el arreglo con false desde la posición 0
        // hasta el final. Así un toque no queda marcado indefinidamente:
        // debe recibirse un nuevo mensaje T para volver a marcarlo.
        Array.Clear( presionadoEsteFrame, 0, presionadoEsteFrame.Length);

        // TryDequeue() intenta sacar el mensaje más antiguo de la cola.
        // Devuelve true si encontró y retiró un mensaje; devuelve false si está vacía.
        //
        // El ciclo procesa todos los mensajes pendientes en el orden recibido.
        while (colaMensajes.TryDequeue(out string mensaje))
        {
            ProcesarMensaje(mensaje);
        }
    }
  
    // ProcesarMensaje(mensaje)
    // Interpreta una línea del Arduino, por ejemplo "T,0" o "R,0".
    // También reconoce los mensajes iniciales READY y ERROR_MPR121.

    private void ProcesarMensaje(string mensaje)
    {
        // READY es el aviso de inicio enviado por Arduino.
        // No es una pulsación, así que se informa y se termina este método.
        if (mensaje == "READY")
        {
            Debug.Log("Arduino y MPR121 iniciaron correctamente.");
            return;
        }

        // ERROR_MPR121 indica que Arduino no pudo iniciar el sensor.
        if (mensaje == "ERROR_MPR121")
        {
            Debug.LogError("Arduino no detectó el MPR121.");
            return;
        }

        // StartsWith() comprueba si el mensaje comienza con el texto indicado.
        // "SYS_ERROR," es un aviso interno generado por este script si falla
        // la lectura serial.
        if (mensaje.StartsWith("SYS_ERROR,"))
        {
            Conectado = false;
            Debug.LogError("Error de comunicación: " + mensaje);
            return;
        }

        // Split(',') separa el texto cada vez que encuentra una coma.
        // Por ejemplo, Split(',') transforma "T,2" en dos textos:
        // partes[0] = "T"
        // partes[1] = "2"
        string[] partes = mensaje.Split(',');

        // El protocolo del juego necesita exactamente dos partes:
        // una letra (T o R) y un número de carril.
        if (partes.Length != 2)
        {
            Debug.LogWarning("Formato de mensaje desconocido: " + mensaje);
            return;
        }

        // TryParse() intenta convertir partes[1] de texto a número entero.
        // Devuelve true si puede hacerlo y guarda el número en carril.
        // Si llega algo como "T,abc", devuelve false sin cerrar el juego.
        if (!int.TryParse(partes[1], out int carril))
        {
            Debug.LogWarning("El carril no es un número válido: " + mensaje);
            return;
        }

        // Comprueba que el carril esté dentro del rango configurado.
        // Con cantidadCarriles = 4, son válidos 0, 1, 2 y 3.
        if (carril < 0 || carril >= cantidadCarriles)
        {
            Debug.LogWarning( "Se recibió el carril " + carril +
                ", pero cantidadCarriles es " + cantidadCarriles + "." );
            return;
        }
               
        // MENSAJE T: SE DETECTÓ UN TOQUE NUEVO

        if (partes[0] == "T")
        {
            // Marca el carril como tocado durante este frame.
            // El controlador de notas consultará este valor para juzgar el toque.
            presionadoEsteFrame[carril] = true;

            // Marca que el botón continúa presionado.
            // Esta marca solo se quitará cuando llegue el mensaje R,carril.
            siguePresionado[carril] = true;

            Debug.Log("Toque recibido en el carril " + carril);
            return;
        }


        // MENSAJE R: SE SOLTÓ EL BOTÓN

        if (partes[0] == "R")
        {
            // Actualiza el estado sostenido del botón.
            // No borra presionadoEsteFrame porque el toque sí ocurrió durante
            // este frame y el juego aún debe poder juzgarlo.
            siguePresionado[carril] = false;
            return;
        }

        // Si el texto antes de la coma no fue T ni R, se desconoce el mensaje.
        Debug.LogWarning("Tipo de mensaje desconocido: " + mensaje);
    }



    // FuePresionadoEsteFrame(carril)
    // Es un método público para que otro script consulte si ocurrió un toque
    // nuevo en cierto carril durante este frame.
    // Ejemplo:
    //   FuePresionadoEsteFrame(0) consulta el primer carril.
    public bool FuePresionadoEsteFrame(int carril)
    {
        // Comprueba que el índice esté dentro del arreglo.
        // Si no lo está, devuelve false en vez de intentar leer una posición
        // que no existe.
        if (carril < 0 || carril >= cantidadCarriles)
        {
            return false;
        }

        // Devuelve el estado guardado para ese carril.
        return presionadoEsteFrame[carril];
    }

    // EstaPresionado(carril)
    // Devuelve true mientras el jugador siga tocando el electrodo.
    // A diferencia de FuePresionadoEsteFrame(), no se limpia al cambiar de frame.

    public bool EstaPresionado(int carril)
    {
        if (carril < 0 || carril >= cantidadCarriles)
        {
            return false;
        }

        return siguePresionado[carril];
    }

    // OnDestroy()
    // Unity llama automáticamente a OnDestroy() cuando este objeto se elimina,
    // cuando se cierra la aplicación o cuando se descarga la escena.
    // Aquí se detiene el hilo y se libera el puerto COM.

    private void OnDestroy()
    {
        // Cambia la bandera para pedir al ciclo while del hilo lector que termine.
        leyendo = false;

        try
        {
            // IsOpen es una propiedad que indica si el puerto sigue abierto.
            // Se comprueba antes de cerrarlo para evitar intentar cerrar un puerto
            // que nunca se abrió o que ya fue cerrado.
            if (puerto != null && puerto.IsOpen)
            {
                // Close() cierra la comunicación activa con el puerto COM.
                // También puede hacer que ReadLine() termine si el hilo estaba
                // esperando datos. Close() cierra la conexión, pero no libera
                // todos los recursos internos del objeto SerialPort.
                puerto.Close();
            }
        }
        catch (Exception error)
        {
            // Si el cierre genera una excepción, muestra el detalle en la consola.
            Debug.LogWarning("Error al cerrar el puerto: " + error.Message);
        }

        // IsAlive es una propiedad del hilo que indica si sigue ejecutándose.
        // No tiene sentido esperar por un hilo que ya terminó o que nunca se creó.
        if (hiloLectura != null && hiloLectura.IsAlive)
        {
            // Join(500) hace que el hilo principal espere a hiloLectura durante
            // un máximo de 500 milisegundos.
            //
            // Join() devuelve true si el hilo terminó dentro de ese tiempo.
            // Devuelve false si los 500 ms se cumplieron y el hilo sigue activo.
            //
            // 500 es un límite máximo, no una pausa fija: si el hilo termina antes,
            // Join() regresa antes. El límite evita que Unity espere indefinidamente.
            bool hiloTermino = hiloLectura.Join(500);

            if (!hiloTermino)
            {
                Debug.LogWarning(
                    "El hilo lector sigue activo después de esperar 500 ms."   );
            }
        }
        // Dispose() libera los recursos internos asociados al objeto SerialPort.
        // Se llama después de Close() porque primero se cierra la conexión activa
        // y después se libera el objeto que administraba esa conexión.
        // Después de Dispose(), este objeto puerto ya no debe volver a utilizarse.
        if (puerto != null)
        {
            puerto.Dispose();
            puerto = null;
        }

        Conectado = false;
    }
}
