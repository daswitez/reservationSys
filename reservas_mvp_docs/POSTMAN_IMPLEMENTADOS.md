# Curls listos para Postman — solo endpoints IMPLEMENTADOS

Solo incluye lo que ya responde en el entorno Docker actual.  
Datos seed de `005_seed_demo.sql`. Formato `--url` explícito para import directo en Postman.

---

## UUIDs seed de referencia

| Variable        | Valor                                |
|-----------------|--------------------------------------|
| TENANT_ID       | 11111111-1111-1111-1111-111111111111 |
| SUPER_ADMIN_ID  | 00000000-0000-0000-0000-000000000001 |
| TENANT_ADMIN_ID | 22222222-2222-2222-2222-222222222222 |
| CLIENT_ID       | 33333333-3333-3333-3333-333333333333 |
| BRANCH_ID       | 44444444-4444-4444-4444-444444444444 |
| SERVICE_ID      | 55555555-5555-5555-5555-555555555555 |
| RESOURCE_ID     | 66666666-6666-6666-6666-666666666666 |
| TENANT_SLUG     | peluqueria-demo                      |

---

## FASE 1 — Bootstrap de plataforma (super_admin)

### 1. Login super admin `[Identity]`
> Primer paso. Obtener el JWT de super_admin para poder crear tenants y admins.

```
curl --request POST --url "http://localhost:5201/auth/login" --header "Content-Type: application/json" --data "{\"email\":\"superadmin@demo.local\",\"password\":\"Password123\"}"
```

---

### 2. Crear tenant `[Identity]`
> Con el token de super_admin, crear la empresa. El slug debe ser único.

```
curl --request POST --url "http://localhost:5201/tenants" --header "Authorization: Bearer PEGAR_SUPER_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"name\":\"Barbería Central\",\"slug\":\"barberia-central\",\"mainCategory\":\"Belleza\",\"timezone\":\"America/La_Paz\",\"status\":\"active\"}"
```

---

### 3. Crear usuario admin del tenant `[Identity]`
> Con el token de super_admin, asignar un admin a la empresa recién creada.

```
curl --request POST --url "http://localhost:5201/users/admin" --header "Authorization: Bearer PEGAR_SUPER_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"firstName\":\"Ana\",\"lastName\":\"Perez\",\"email\":\"ana.perez@example.com\",\"phone\":\"+59170000101\",\"password\":\"Password123\"}"
```

---

## FASE 2 — Configuración del negocio (tenant_admin)

### 4. Login tenant admin `[Identity]`
> El admin recién creado (o el seed) inicia sesión para obtener su JWT.

```
curl --request POST --url "http://localhost:5201/auth/login" --header "Content-Type: application/json" --data "{\"email\":\"admin@demo.local\",\"password\":\"Password123\"}"
```

---

### 5. Crear sucursal `[Catalog]`
> Con el JWT de tenant_admin, crear la primera ubicación física. El tenant_id se toma del JWT.

```
curl --request POST --url "http://localhost:5202/branches" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"name\":\"Sucursal Norte\",\"address\":\"Av. Norte #456\",\"phone\":\"+59171111111\",\"emailContact\":\"norte@peluqueria.com\",\"timezone\":\"America/La_Paz\",\"status\":\"active\"}"
```

---

### 6. Crear servicio `[Catalog]`
> Con el JWT de tenant_admin, crear el primer servicio ofrecido. durationMinutes debe ser > 0.

```
curl --request POST --url "http://localhost:5202/services" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"name\":\"Corte de cabello\",\"description\":\"Corte clásico o moderno\",\"durationMinutes\":30,\"referencePrice\":50.00,\"modality\":\"presencial\",\"status\":\"active\"}"
```

---

## FASE 3 — Verificación del catálogo (admin)

### 7. Listar sucursales `[Catalog]`
> Admin verifica que las sucursales del tenant quedaron bien creadas.

```
curl --request GET --url "http://localhost:5202/branches?status=active" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```

---

### 8. Ver sucursal por ID `[Catalog]`
> Admin ve el detalle completo de la sucursal seed.

```
curl --request GET --url "http://localhost:5202/branches/44444444-4444-4444-4444-444444444444" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```

---

### 9. Listar servicios `[Catalog]`
> Admin verifica que los servicios del tenant quedaron bien creados.

```
curl --request GET --url "http://localhost:5202/services?status=active" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```

---

### 10. Ver servicio por ID `[Catalog]`
> Admin ve el detalle completo del servicio seed.

```
curl --request GET --url "http://localhost:5202/services/55555555-5555-5555-5555-555555555555" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```

---

## FASE 4 — Portal público (sin autenticación)

### 11. Listar tenants públicos `[Identity]`
> Cualquiera puede ver qué empresas están activas en la plataforma.

```
curl --request GET --url "http://localhost:5201/tenants/public"
```

---

### 12. Ver sucursales públicas del tenant `[Catalog]`
> Cualquiera puede ver las sucursales activas de un negocio por su slug.

```
curl --request GET --url "http://localhost:5202/public/tenants/peluqueria-demo/branches"
```

---

### 13. Ver servicios públicos del tenant `[Catalog]`
> Cualquiera puede ver el catálogo de servicios activos del negocio.

```
curl --request GET --url "http://localhost:5202/public/tenants/peluqueria-demo/services"
```

---

## FASE 5 — Registro y sesión del cliente

### 14. Registrar cliente `[Identity]`
> El cliente crea su cuenta global. Email único en toda la plataforma.

```
curl --request POST --url "http://localhost:5201/auth/register-client" --header "Content-Type: application/json" --data "{\"firstName\":\"Daniel\",\"lastName\":\"Mercado\",\"email\":\"daniel.test@example.com\",\"phone\":\"+59170000000\",\"password\":\"Password123\"}"
```

---

### 15. Login cliente `[Identity]`
> El cliente recién registrado (o el seed) inicia sesión.

```
curl --request POST --url "http://localhost:5201/auth/login" --header "Content-Type: application/json" --data "{\"email\":\"cliente@demo.local\",\"password\":\"Password123\"}"
```

---

### 16. Ver mi perfil `[Identity]`
> El cliente verifica sus propios datos con el token obtenido.

```
curl --request GET --url "http://localhost:5201/auth/me" --header "Authorization: Bearer PEGAR_TOKEN_AQUI"
```

---

### 17. Editar mi perfil `[Identity]`
> El cliente actualiza su nombre, apellido, email o teléfono.

```
curl --request PUT --url "http://localhost:5201/users/me" --header "Authorization: Bearer PEGAR_TOKEN_AQUI" --header "Content-Type: application/json" --data "{\"firstName\":\"Daniel\",\"lastName\":\"Mercado\",\"email\":\"daniel.test@example.com\",\"phone\":\"+59170000103\"}"
```

---

### 18. Cambiar mi contraseña `[Identity]`
> Invalida todos los JWT anteriores. Volver a hacer login después.

```
curl --request PATCH --url "http://localhost:5201/users/me/password" --header "Authorization: Bearer PEGAR_TOKEN_AQUI" --header "Content-Type: application/json" --data "{\"currentPassword\":\"Password123\",\"newPassword\":\"Password456\"}"
```

---

## FASE 6 — Gestión de usuarios (admin)

### 19. Listar usuarios `[Identity]`
> super_admin ve todos; tenant_admin ve solo su tenant.

```
curl --request GET --url "http://localhost:5201/users?tenantId=11111111-1111-1111-1111-111111111111&role=tenant_admin&status=active&offset=0&limit=20" --header "Authorization: Bearer PEGAR_TOKEN_AQUI"
```

---

### 20. Ver usuario por ID `[Identity]`
> Admin ve el detalle completo del usuario seed tenant_admin.

```
curl --request GET --url "http://localhost:5201/users/22222222-2222-2222-2222-222222222222" --header "Authorization: Bearer PEGAR_TOKEN_AQUI"
```

---

### 21. Validar acceso a sucursal `[Identity]`
> Verifica si el usuario del token tiene permiso admin sobre la sucursal seed.

```
curl --request GET --url "http://localhost:5201/auth/access/branches/44444444-4444-4444-4444-444444444444" --header "Authorization: Bearer PEGAR_TOKEN_AQUI"
```

---

### 22. Editar usuario `[Identity]`
> Admin actualiza nombre, apellido, email y teléfono de otro usuario.

```
curl --request PUT --url "http://localhost:5201/users/22222222-2222-2222-2222-222222222222" --header "Authorization: Bearer PEGAR_SUPER_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"firstName\":\"Administrador\",\"lastName\":\"Actualizado\",\"email\":\"admin@demo.local\",\"phone\":\"+59170000102\"}"
```

---

### 23. Cambiar estado de usuario `[Identity]`
> Activa, desactiva o bloquea. Los JWT del usuario quedan inválidos al desactivar/bloquear.

```
curl --request PATCH --url "http://localhost:5201/users/22222222-2222-2222-2222-222222222222/status" --header "Authorization: Bearer PEGAR_SUPER_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"status\":\"blocked\"}"
```

---

## FASE 7 — Actualizaciones del catálogo

### 24. Actualizar sucursal `[Catalog]`
> Admin reemplaza todos los campos editables de una sucursal.

```
curl --request PUT --url "http://localhost:5202/branches/44444444-4444-4444-4444-444444444444" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"name\":\"Sucursal Centro Actualizada\",\"address\":\"Av. Principal #999\",\"phone\":\"+59170000200\",\"emailContact\":\"centro.nuevo@peluqueria.com\",\"timezone\":\"America/La_Paz\",\"status\":\"active\"}"
```

---

### 25. Cambiar estado de sucursal `[Catalog]`
> Una sucursal inactiva desaparece del portal público de inmediato.

```
curl --request PATCH --url "http://localhost:5202/branches/44444444-4444-4444-4444-444444444444/status" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"status\":\"inactive\"}"
```

---

### 26. Actualizar servicio `[Catalog]`
> Admin reemplaza todos los campos editables de un servicio.

```
curl --request PUT --url "http://localhost:5202/services/55555555-5555-5555-5555-555555555555" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"name\":\"Corte premium\",\"description\":\"Corte y asesoramiento personalizado\",\"durationMinutes\":45,\"referencePrice\":75.50,\"modality\":\"presencial\",\"status\":\"active\"}"
```

---

### 27. Cambiar estado de servicio `[Catalog]`
> Un servicio inactivo no aparece en el portal público ni acepta reservas.

```
curl --request PATCH --url "http://localhost:5202/services/55555555-5555-5555-5555-555555555555/status" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN" --header "Content-Type: application/json" --data "{\"status\":\"inactive\"}"
```

---

## FASE 8 — Bajas lógicas

### 28. Eliminar usuario `[Identity]`
> Baja lógica: estado pasa a inactive, JWT invalidados. Responde 204 sin body.

```
curl --request DELETE --url "http://localhost:5201/users/22222222-2222-2222-2222-222222222222" --header "Authorization: Bearer PEGAR_SUPER_ADMIN_TOKEN"
```

---

### 29. Eliminar sucursal `[Catalog]`
> Baja lógica: la sucursal se desactiva pero se conservan sus relaciones. Devuelve DTO actualizado.

```
curl --request DELETE --url "http://localhost:5202/branches/44444444-4444-4444-4444-444444444444" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```

---

### 30. Eliminar servicio `[Catalog]`
> Baja lógica: el servicio se desactiva pero se conservan reservas y auditoría asociadas. Devuelve DTO actualizado.

```
curl --request DELETE --url "http://localhost:5202/services/55555555-5555-5555-5555-555555555555" --header "Authorization: Bearer PEGAR_TENANT_ADMIN_TOKEN"
```
