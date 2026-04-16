# AuthService

.NET 10 ile yazılmış, JWT Bearer tabanlı kimlik doğrulama ve yetkilendirme servisi.

## Teknolojiler

| Katman | Teknoloji |
|---|---|
| Framework | .NET 10 Minimal API |
| Kimlik Yönetimi | ASP.NET Core Identity |
| Veritabanı | SQL Server + EF Core 10 |
| Token | JWT Bearer + Refresh Token (rotation) |
| Validasyon | FluentValidation |
| Loglama | Serilog (structured, JSON) |
| Gözlemlenebilirlik | OpenTelemetry (OTLP) |
| API Dokümantasyonu | Scalar (`/scalar/v1`) |
| Konteyner | Docker |
| Orkestrasyon | Kubernetes + Helm |

---

## Proje Yapısı

```
AuthService/
├── Data/
│   ├── AppDbContext.cs        # Identity + RefreshToken tabloları
│   └── DbSeeder.cs            # Rol ve admin kullanıcı seed
├── Endpoints/
│   └── Endpoints.cs           # Auth + User endpoint'leri, validatörler, exception handler
├── Models/
│   └── Models.cs              # AppUser, RefreshToken, request/response DTO'ları
├── Options/
│   └── JwtOptions.cs          # Strongly-typed JWT konfigürasyonu
├── Services/
│   └── Services.cs            # ITokenService, TokenService, IAuthService, AuthService
├── helm/
│   ├── values.yaml
│   └── templates/
│       ├── deployment.yaml
│       └── migration-job.yaml # pre-install/pre-upgrade Helm hook
├── Program.cs
├── appsettings.json
├── appsettings.Production.json
└── Dockerfile

AuthService.Migrator/          # Migration + seed runner (K8s Job)
├── Program.cs
├── Dockerfile
└── AuthService.Migrator.csproj

AuthService.IntegrationTests/  # xUnit + Testcontainers
├── Infrastructure/
│   └── AuthWebApplicationFactory.cs
└── Tests/
    ├── AuthEndpointTests.cs
    └── TokenServiceTests.cs
```

---

## API Endpoint'leri

### Auth

| Method | Path | Auth | Açıklama |
|---|---|---|---|
| `POST` | `/auth/register` | — | Yeni kullanıcı kaydı |
| `POST` | `/auth/login` | — | Giriş, token çifti döner |
| `POST` | `/auth/refresh` | — | Access token yeniler (rotation) |
| `POST` | `/auth/revoke` | JWT | Refresh token iptal eder |

### Kullanıcı

| Method | Path | Auth | Açıklama |
|---|---|---|---|
| `GET` | `/users/me` | JWT | Oturum açmış kullanıcı bilgisi |

### Sistem

| Path | Açıklama |
|---|---|
| `/health/live` | Uygulama ayakta mı? |
| `/health/ready` | DB bağlantısı dahil hazır mı? |
| `/scalar/v1` | API arayüzü (yalnızca Development) |
| `/openapi/v1.json` | OpenAPI spec (yalnızca Development) |

### Örnek İstekler

**Kayıt**
```http
POST /auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "Guclu@Sifre123!",
  "firstName": "Ada",
  "lastName": "Yılmaz"
}
```

**Giriş**
```http
POST /auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "Guclu@Sifre123!"
}
```

**Yanıt**
```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "base64-encoded-token",
  "accessTokenExpiry": "2025-01-01T12:15:00Z",
  "tokenType": "Bearer"
}
```

**Token Yenileme**
```http
POST /auth/refresh
Content-Type: application/json

{
  "refreshToken": "base64-encoded-token"
}
```

**Token İptal**
```http
POST /auth/revoke
Authorization: Bearer eyJhbGci...
Content-Type: application/json

{
  "refreshToken": "base64-encoded-token"
}
```

---

## Güvenlik

### Refresh Token

- Plain-text DB'ye yazılmaz; **SHA-256 hash** olarak saklanır.
- Her kullanımda **rotation** uygulanır: eski token iptal edilir, yeni token üretilir.
- Süresi dolmuş veya iptal edilmiş tokenlar her login işleminde otomatik temizlenir.
- Revoke edilmiş bir token tekrar kullanılmaya çalışılırsa kullanıcıya ait **tüm tokenlar** iptal edilir.

### Şifre Politikası

Minimum 12 karakter, büyük harf, küçük harf, rakam ve özel karakter zorunludur.

### Hesap Kilitleme

5 başarısız giriş denemesinde hesap 15 dakika kilitlenir.

---

## Yerel Geliştirme

**Gereksinimler:** Docker Desktop

```bash
# Tüm servisleri ayağa kaldır (SQL Server + Jaeger dahil)
docker compose up

# Servis portları:
# Auth Service → http://localhost:8001
# Jaeger UI    → http://localhost:16686
# SQL Server   → localhost:1433
```

Servis ayağa kalktığında migration ve seed otomatik çalışır.
Admin kullanıcı bilgileri `docker-compose.yml` içindeki `Seed__AdminEmail` / `Seed__AdminPassword` değerlerinden okunur.

---

## Veritabanı Migration

Migration üretmek için Ana projeden:

```bash
dotnet ef migrations add <MigrationAdi> --project AuthService
```

Migration'lar uygulamaya **dahil edilmez**; `AuthService.Migrator` aracılığıyla çalıştırılır.

---

## Testler

```bash
# Tüm testler
dotnet test

# Sadece unit testler (container gerektirmez)
dotnet test --filter "FullyQualifiedName!~AuthEndpointTests"

# Sadece integration testler (Docker gerektirir)
dotnet test --filter "FullyQualifiedName~AuthEndpointTests"

# Coverage raporu
dotnet test --collect:"XPlat Code Coverage"
```

Integration testler **Testcontainers** kullanır — test sırasında otomatik olarak bir SQL Server container ayağa kalkar ve test bitince silinir.

---

## Konfigürasyon

| Değişken | Açıklama | Varsayılan |
|---|---|---|
| `Jwt__SecretKey` | İmzalama anahtarı (min. 32 karakter) | — |
| `Jwt__Issuer` | Token issuer | `auth-service` |
| `Jwt__Audience` | Token audience | `api-clients` |
| `Jwt__AccessTokenExpiryMinutes` | Access token ömrü (dakika) | `15` |
| `Jwt__RefreshTokenExpiryDays` | Refresh token ömrü (gün) | `7` |
| `ConnectionStrings__Default` | SQL Server bağlantı dizesi | — |
| `Seed__AdminEmail` | İlk admin kullanıcı e-postası | — |
| `Seed__AdminPassword` | İlk admin kullanıcı şifresi | — |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry collector adresi | — |

> **Uyarı:** `Jwt__SecretKey`, `ConnectionStrings__Default` ve seed bilgileri production ortamında asla `appsettings.json` içine yazılmamalıdır. Kubernetes Secret veya External Secrets Operator ile yönetilmelidir.

---

## Kubernetes ile Deploy

```bash
# Staging
helm upgrade auth-service ./helm \
  --install \
  --namespace auth \
  --create-namespace \
  --atomic \
  --set image.tag=<git-sha> \
  --set secrets.jwtSecretKey="<secret>" \
  --set secrets.connectionString="<connection-string>"
```

`helm upgrade` komutu çalıştırıldığında önce `migration-job` (`pre-install`/`pre-upgrade` hook) çalışır, migration ve seed tamamlandıktan sonra deployment rollout başlar.

### Roller

| Rol | Açıklama |
|---|---|
| `Admin` | Tüm işlemler |
| `User` | Standart kullanıcı işlemleri |
| `ReadOnly` | Yalnızca okuma |

Yeni kayıt olan kullanıcılara otomatik olarak `User` rolü atanır.

---

## CI/CD

`.github/workflows/ci-cd.yml` dosyası 4 aşamalı bir pipeline tanımlar:

```
test → docker → deploy-staging → deploy-production
                                        ↑
                              Manuel onay gerektirir
```

| Aşama | Tetikleyici | Açıklama |
|---|---|---|
| `test` | Her push/PR | Build + unit + integration testler, coverage |
| `docker` | `main`/`develop` push | Multi-platform build, GHCR push, Trivy güvenlik taraması |
| `deploy-staging` | `main` push | Helm upgrade, smoke test |
| `deploy-production` | `main` push + manuel onay | Helm upgrade, smoke test, Slack bildirimi |

### Gerekli GitHub Secrets

```
KUBE_CONFIG_STAGING / KUBE_CONFIG_PROD
JWT_SECRET_KEY_STAGING / JWT_SECRET_KEY_PROD
DB_CONNECTION_STRING_STAGING / DB_CONNECTION_STRING_PROD
ADMIN_EMAIL_STAGING / ADMIN_EMAIL_PROD
ADMIN_PASSWORD_STAGING / ADMIN_PASSWORD_PROD
SLACK_WEBHOOK
```
