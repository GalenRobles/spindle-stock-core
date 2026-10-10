Roceel - Sistema de Gestión de Inventario Industrial
Roceel es una plataforma de gestión de inventario en tiempo real diseñada para el mantenimiento industrial (PLC, CNC, servomotores). Conecta directamente las recepciones de compras con el almacén mediante una arquitectura orientada a eventos.
Tecnologías
Backend/Frontend: C# .NET 8 (Razor Pages)
Base de Datos: PostgreSQL + Entity Framework Core
Mensajería: Apache Kafka
Despliegue: Docker
Instalación y Ejecución Rápida
Para levantar el proyecto completo (Base de datos, Kafka y la aplicación web) utilizando el kit proporcionado, ejecuta el siguiente comando en la raíz del repositorio:
```bash
docker compose up --build -d
```
Accesos
Aplicación Web (Razor Pages): `http://localhost:5012`
Evaluador de Pruebas (Contract Tests):
```bash
  docker compose run --rm contract-tests --api http://host.docker.internal:5012
  ```
