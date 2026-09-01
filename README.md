# Projekt 1 — Medicinski sustav (Ishodi 1, 4, 5)

Konzolna .NET 8 aplikacija + **vlastiti ORM (MiniOrm)** + **PostgreSQL u Dockeru**.

Projekt pokriva relacijsku bazu, složeni konceptualni model i ORM

## Pokretanje

```bash
cd Projekt1-MedicinskiSustav
docker compose up -d
dotnet run --project src/MedicalApp/MedicalApp.csproj
```

Postgres sluša na **localhost:5433** (da ne sudara s lokalnim Postgresom na 5432).

Pri **prvom pokretanju** aplikacija:

1. uspoređuje klase entiteta sa shemom i izvršava migracije (code-first)
2. traži unos liječnika (jedina prilika — kasnije CRUD nad liječnicima nije dostupan)

U izborniku: `9` učitava demo pacijenta za obranu.

## Model

| Entitet | Relacije | CRUD |
|---|---|---|
| `Doctor` | 1:N Examination | samo seed pri prvom startu |
| `Address` | N:1 s Patient (boravište i prebivalište) | da |
| `Patient` | N:1 Address (2x), 1:N Illness, 1:N Examination | da |
| `Illness` | N:1 Patient, 1:N Medication | da |
| `Medication` | N:1 Illness (doza, jedinica, učestalost) | da |
| `Examination` | N:1 Patient, N:1 Doctor, tip CT/MR/ULTRA/EKG/ECHO/OKO/DERM/DENTA/MAMMO/EEG | da |

OIB je `CHAR(11) UNIQUE`. Tipovi pokrivaju INT, DECIMAL, FLOAT, VARCHAR, CHAR, TEXT, TIMESTAMPTZ i TIMESTAMP.

## MiniOrm — što demonstrirati na obrani

### Mapiranje (refleksija)

Atributi `[Table]`, `[Column]`, `[Key]`, `[Identity]`, `[NotNull]`, `[Unique]`, `[SqlDefault]`, `[ForeignKey]`, `[HasMany]`, `[BelongsTo]` čitaju se refleksijom u `MetadataCache`.

### Filtriranje (expression trees)

`db.Patients.Where(p => p.LastName.Contains("ić")).OrderBy(p => p.FirstName)` prevodi se u `WHERE` / `ORDER BY` s parametriziranim SQL-om (`WhereTranslator`).

### Change tracking

`ChangeTracker` drži snapshot originalnih vrijednosti. `SaveChanges()` radi `DetectChanges()` i generira `UPDATE` **samo za izmijenjene stupce**. Izbornik `7` to pokazuje bez spremanja.

### Eager vs lazy

| | Eager (`Include`) | Lazy (`LazyLoadList`) |
|---|---|---|
| Kad se učitava | Odmah, uz roditelja | Pri prvom pristupu kolekciji |
| SQL | dodatni `IN (...)` upiti | 1 upit po kolekciji |
| Prednost | predvidiv broj round-tripova | ne učitava što ne treba |
| Nedostatak | veći payload, unaprijed treba znati grafa | **N+1** ako se radi u petlji |

### Migracije

`MigrationRunner` čita `information_schema`, uspoređuje s klasama i generira UP/DOWN SQL. Stanje je u `__miniorm_migrations`.

Migracija **ne može** (ili ne smije) proći kad:

- dodajete `NOT NULL` stupac bez DEFAULT, a tablica već ima retke
- pretvarate tip nespojivo (npr. TEXT → INTEGER s nebrojčanim vrijednostima)
- dodajete UNIQUE po stupcu koji već ima duplikate
- DROP stupca/tablice bi prekršio FK (osim uz CASCADE)
- rollback ne može vratiti obrisane podatke

Pretpostavke generatora (dopuštene zadatkom): rename stupca tretira se kao drop+add; ne pokušava se heuristika preimenovanja.


## Struktura

```
Projekt1-MedicinskiSustav/
  docker-compose.yml
  MedicinskiSustav.sln
  src/MiniOrm/          # biblioteka ORM-a
  src/MedicalApp/       # konzolni CRUD
```
