
# Security Testing Guide – Order Management API

## Formål

Dette dokument beskriver, hvordan de bevidst introducerede sikkerhedssårbarheder i Order Management API'et kan testes og verificeres.

Applikationen er udviklet til undervisning i Continuous Integration (CI) og DevSecOps og indeholder med vilje sikkerhedsmæssigt problematisk kode.

Formålet med testene er at:

1. Verificere, at de tilsigtede sårbarheder eksisterer.
2. Undersøge, hvordan sårbarhederne kan udnyttes.
3. Dokumentere de forventede resultater.
4. Sammenligne manuel sikkerhedstest med statisk kodeanalyse (SAST).

> ⚠️ **Bemærk:** Testene skal udelukkende udføres på den lokale undervisningsapplikation. Applikationen er bevidst usikker og må ikke eksponeres offentligt.

---

## Forudsætninger

- .NET 10 SDK
- PowerShell
- Order Management API
- SQLite in-memory
- Applikationen skal køre lokalt

### Start applikationen

Åbn en terminal i solution-mappen og kør:

```powershell
dotnet run --project OrderManagement.Api
```

Applikationen skal starte i `Testing`-miljøet via den konfigurerede `launchSettings.json`.

Eksempel på konsoloutput:

```text
Now listening on: http://localhost:5000
Hosting environment: Testing
```

API'et kan derefter tilgås på:

http://localhost:5000

Swagger UI:

http://localhost:5000/swagger

> Da databasen er en SQLite in-memory-database, forsvinder data, når applikationen stoppes. Testdata skal derfor oprettes igen efter genstart.

---

# Test 1 – SQL Injection

## Formål

At verificere, at SQL-forespørgsler kan manipuleres gennem brugerinput.

**Endpoint:**

`GET /api/orders/search?customerName=...`

Sårbarheden skyldes, at brugerinput indsættes direkte i en SQL-forespørgsel uden parameterisering.

## Trin 1 – Opret testdata

Opret en ordre til Alice:

```powershell
$body = @{
    customerName = "Alice"
    productName = "Keyboard"
    quantity = 2
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Opret derefter en ordre til Bob:

```powershell
$body = @{
    customerName = "Bob"
    productName = "Mouse"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Begge requests bør gennemføres og oprette en ordre med status `Created`.

## Trin 2 – Normal søgning

Søg efter Alice:

```powershell
Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/search?customerName=Alice"
```

**Forventet resultat:**

Kun ordren til Alice returneres.

## Trin 3 – SQL Injection

Send følgende request:

```powershell
Invoke-RestMethod `
    -Uri ("http://localhost:5000/api/orders/search?customerName=" +
          [uri]::EscapeDataString("' OR 1=1 --"))
```

Payload:

```sql
' OR 1=1 --
```

Den resulterende SQL-forespørgsel bliver:

```sql
SELECT * FROM Orders
WHERE CustomerName = '' OR 1=1 --'
```

### Forklaring

- `'` afslutter den eksisterende SQL-streng.
- `OR 1=1` introducerer en betingelse, som altid er sand.
- `--` udkommenterer resten af SQL-forespørgslen.

## Forventet resultat

API'et returnerer både Alice og Bob.

Dermed har brugerinput ændret SQL-forespørgslens logik.

### Verifikation

**Status: VERIFICERET**

SQL Injection er demonstreret ved en HTTP-request, der returnerede begge oprettede ordrer.

### Sikker løsning

Anvend parameteriserede SQL-forespørgsler, eksempelvis:

```csharp
var results = await db.Orders
    .FromSqlInterpolated(
        $"SELECT * FROM Orders WHERE CustomerName = {customerName}")
    .ToListAsync();
```

Alternativt kan der anvendes LINQ:

```csharp
var results = await db.Orders
    .Where(o => o.CustomerName == customerName)
    .ToListAsync();
```

---

# Test 2 – Path Traversal

## Formål

At undersøge, om en klient kan læse filer uden for den mappe, som API'et er beregnet til at give adgang til.

**Endpoint:**

`GET /api/orders/documents?fileName=...`

Applikationen indeholder følgende kode:

```csharp
var path = Path.Combine(
    environment.ContentRootPath,
    "Documents",
    fileName);

var content = await System.IO.File.ReadAllTextAsync(path);
```

Problemet er, at `fileName` kommer direkte fra klienten og anvendes til at konstruere en filsti.

## Trin 1 – Hent et legitimt dokument

Send følgende request:

```powershell
Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/documents?fileName=order-info.txt"
```

### Forventet resultat

API'et returnerer indholdet af:

```text
OrderManagement.Api/Documents/order-info.txt
```

Dette demonstrerer, at endpointet fungerer som tiltænkt.

## Trin 2 – Forsøg Path Traversal

Vi forsøger nu at hente applikationens `Program.cs`, som ligger uden for `Documents`-mappen.

```powershell
$fileName = [uri]::EscapeDataString("../Program.cs")

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/documents?fileName=$fileName"
```

Payload:

```text
../Program.cs
```

### Forklaring

`..` angiver den overliggende mappe i filsystemet.

Den konstruerede sti bliver konceptuelt:

```text
OrderManagement.Api/
├── Program.cs
└── Documents/
    └── order-info.txt
```

Når `../Program.cs` kombineres med `Documents`-mappen, peger stien på `Program.cs` i applikationens rodmappe.

### Forventet resultat

API'et returnerer kildekoden fra `Program.cs`.

Dette viser, at klienten kan få adgang til filer uden for den tilsigtede mappe.

## Verifikation

**Status: AFVENTER MANUEL TEST**

Sårbarheden forventes at kunne udnyttes på baggrund af implementeringen, men resultatet skal verificeres gennem en faktisk HTTP-request.

### Sikker løsning

En mulig løsning er at begrænse filadgangen til kendte dokumenter eller anvende en kontrolleret mapping mellem dokument-ID'er og filnavne.

Hvis klienten skal kunne angive et filnavn, skal den resulterende sti valideres, så den ikke kan pege uden for den tilladte dokumentmappe.

Brug af `Path.GetFileName()` kan være en del af en begrænset løsning, men en allowlist af tilladte dokumenter er ofte mere robust.

---


# Test 3 – Information Disclosure

## Formål

At undersøge, om applikationen afslører interne tekniske oplysninger gennem fejlmeddelelser.

**Endpoint:**

`GET /api/orders/documents?fileName=...`

Applikationen indeholder følgende exception-håndtering:

```csharp
catch (Exception ex)
{
    return StatusCode(500, ex.ToString());
}
```

Problemet er, at `ex.ToString()` kan indeholde:

- Stack traces
- Interne filstier
- Klassenavne
- Metodenavne
- Linjenumre
- Tekniske detaljer om applikationens implementering

Disse oplysninger bør normalt ikke eksponeres til klienten.

## Trin 1 – Fremprovoker en exception

Forsøg at hente en fil, der ikke eksisterer.

Åbn PowerShell, og udfør følgende kommando:

```powershell
curl.exe -i "http://localhost:5000/api/orders/documents?fileName=does-not-exist.txt"
```

Parameteren `-i` får curl til at vise både HTTP-response headers og response body.

Dermed kan vi undersøge, hvilke oplysninger serveren returnerer, selvom den svarer med en HTTP-fejlkode.

## Forventet resultat

Serveren returnerer HTTP `500 Internal Server Error`.

Response body indeholder en fejlmeddelelse med interne tekniske oplysninger.

**Faktisk observeret resultat:**

```text
HTTP/1.1 500 Internal Server Error
Content-Type: text/plain; charset=utf-8
Server: Kestrel

System.IO.FileNotFoundException:
Could not find file 'C:\source\ci_project\OrderManagement\OrderManagement.Api\Documents\does-not-exist.txt'.

File name: 'C:\source\ci_project\OrderManagement\OrderManagement.Api\Documents\does-not-exist.txt'

   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(...)
   at System.IO.File.InternalReadAllTextAsync(...)
   at OrderManagement.Api.Controllers.OrdersController.GetDocument(String fileName)
      in C:\source\ci_project\OrderManagement\OrderManagement.Api\Controllers\OrdersController.cs:line 102
```

Ovenstående er et forkortet uddrag af det faktiske HTTP-response fra testen.

Det præcise indhold afhænger af operativsystemet, .NET-versionen og applikationens placering på filsystemet.

## Trin 2 – Undersøg informationsindholdet

Undersøg HTTP-response, og identificer hvilke interne oplysninger der afsløres.

| Information | Observeret resultat |
|---|---|
| Exception-type | `System.IO.FileNotFoundException` |
| Intern filsti | `C:\source\ci_project\OrderManagement\...` |
| Controller | `OrdersController` |
| Metode | `GetDocument()` |
| Kildekodefil | `OrdersController.cs` |
| Linjenummer | `102` |
| Stack trace | Returneres til klienten |

### Sikkerhedsmæssig konsekvens

En angriber kan anvende disse oplysninger til at få indsigt i applikationens interne struktur og implementering.

Eksempelvis afsløres:

- Hvordan applikationens mapper er organiseret.
- Hvilke interne klasser og metoder der anvendes.
- Hvor i kildekoden en exception opstår.
- Hvilke .NET-komponenter der indgår i behandlingen af requesten.

Disse oplysninger kan hjælpe en angriber med at identificere yderligere angrebsmuligheder.

Det er vigtigt at understrege, at en HTTP `500`-statuskode ikke i sig selv udgør en Information Disclosure-sårbarhed.

Sårbarheden består i, at serveren returnerer interne oplysninger i response body.

### Verifikation

**Status: VERIFICERET**

Sårbarheden er verificeret gennem en HTTP-request til applikationens dokument-endpoint.

Testen demonstrerede, at applikationen returnerer:

- HTTP `500 Internal Server Error`.
- En detaljeret exception-meddelelse.
- Interne filstier.
- Klassenavne og metodenavne.
- Stack trace med kildekodefil og linjenummer.

Dermed er det påvist, at applikationen eksponerer interne tekniske oplysninger til klienten.

Det er endnu ikke verificeret, om CodeQL identificerer den konkrete implementering.

## Sikker løsning

Interne exceptions bør logges på serveren, mens klienten modtager en generisk fejlmeddelelse.

Eksempel:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Error retrieving order document");

    return Problem(
        title: "An unexpected error occurred.",
        statusCode: 500);
}
```

Eksemplet forudsætter, at en `ILogger` er tilgængelig i controlleren.

Denne tilgang sikrer, at udviklerne stadig kan undersøge den interne fejl gennem applikationens logs, uden at de detaljerede oplysninger returneres til klienten.

Det er samtidig vigtigt at anvende korrekte HTTP-statuskoder ved forventede fejl.

Eksempelvis bør et ikke-eksisterende dokument normalt resultere i `404 Not Found` frem for `500 Internal Server Error`.

### Sammenhæng med miljøkonfiguration

I vores applikation er Developer Exception Page konfigureret til kun at være aktiv i ikke-produktionsmiljøer.

Den aktuelle sårbarhed skyldes imidlertid controllerens egen exception-håndtering:

```csharp
return StatusCode(500, ex.ToString());
```

Derfor kan de detaljerede fejlmeddelelser også blive eksponeret i Production, selvom Developer Exception Page er deaktiveret.

Dette demonstrerer, hvorfor sikker fejlhåndtering skal implementeres konsekvent i hele applikationen.

### Faglig kobling

Sårbarheden relaterer sig til:

- **Secure by Design, kapitel 9:** Handling failures securely.
- **OWASP Code Review Guide v2:** Error Handling.
- **OWASP ASVS 5.0.0:** Krav til sikker fejlhåndtering og begrænsning af informationslækage.

Den illustrerer, hvordan uhensigtsmæssig fejlhåndtering kan kompromittere fortrolighed (Confidentiality), selvom den oprindelige fejl ikke nødvendigvis skyldes et angreb.

---


# Test 4 – NuGet Vulnerability Scanning

## Formål

At verificere, at applikationen indeholder en kendt sårbar tredjepartsafhængighed.

Applikationen indeholder følgende afhængighed:

```xml
<PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
```

Denne version er forbundet med:

- CVE-2024-21907
- Høj alvorlighedsgrad
- En denial-of-service-sårbarhed

## Trin 1 – Restore afhængigheder

```powershell
dotnet restore
```

Undersøg eventuelle sikkerhedsadvarsler i outputtet.

## Trin 2 – Undersøg sårbare afhængigheder

```powershell
dotnet list OrderManagement.Api/OrderManagement.Api.csproj package --vulnerable --include-transitive
```

### Forventet resultat

NuGet Audit identificerer den sårbare version af `Newtonsoft.Json`.

Undersøg den rapporterede sikkerhedsrådgivning, herunder alvorlighedsgrad og tilgængelige rettede versioner.

### Verifikation

**Status: AFVENTER NUGET AUDIT**

## Sikker løsning

Opdater afhængigheden til en version, hvor den identificerede sårbarhed er rettet.

Kør derefter scanningen igen, og verificer, at fundet er håndteret.

---


# Test 5 – Statisk sikkerhedsanalyse med CodeQL

## Formål

At undersøge, om CodeQL kan identificere sikkerhedssårbarheder i applikationens kildekode uden at eksekvere selve applikationen.

CodeQL er et værktøj til Static Application Security Testing (SAST), der analyserer kildekode for blandt andet usikre kodekonstruktioner og potentielt farlige dataflows.

I denne test anvender vi **CodeQL CLI** til at udføre analysen lokalt på en Windows-maskine.

Det gør det muligt at verificere de bevidst introducerede sårbarheder uden at skulle oprette et GitHub-repository eller konfigurere GitHub Actions.

Vi ønsker at undersøge følgende sårbarheder:

| Sårbarhed | CodeQL-regel |
|---|---|
| SQL Injection | `cs/sql-injection` |
| Path Traversal | `cs/path-injection` |
| Information Disclosure | `cs/information-exposure-through-exception` |

Bemærk, at en sårbarhed godt kan eksistere, selvom CodeQL ikke identificerer den.

---

## Forudsætninger

- Windows med PowerShell
- .NET 10 SDK
- Det færdige Order Management API
- CodeQL CLI
- CodeQLs analysepakke til C#

Applikationen skal kunne bygges med `dotnet build`, inden CodeQL-databasen oprettes.

Det er ikke nødvendigt at starte selve Web API'et.

---

## Trin 1 – Installer CodeQL CLI

Download CodeQL CLI fra GitHubs officielle repository:

https://github.com/github/codeql-cli-binaries/releases

Den version, som blev anvendt under udviklingen af undervisningsmaterialet, er:

https://github.com/github/codeql-cli-binaries/releases/tag/v2.27.4

Download Windows-arkivet:

`codeql-win64.zip`

Pak arkivet ud, eksempelvis til:

`C:\tools\codeql\`

Kontroller, at følgende fil findes:

`C:\tools\codeql\codeql.exe`

### Kontroller installationen

Åbn PowerShell, og kør:

```powershell
C:\tools\codeql\codeql.exe version
```

Kommandoen skal vise den installerede CodeQL-version.

Hvis dette fungerer, er CodeQL CLI installeret korrekt.

### Bemærkning om CodeQL-pakker

Den selvstændige CodeQL CLI indeholder ikke nødvendigvis alle de analysepakker, der skal bruges til at analysere C#.

GitHub tilbyder også et samlet CodeQL bundle, hvor CLI og kompatible analysepakker er inkluderet.

I denne vejledning anvendes den selvstændige CLI sammen med den relevante C#-analysepakke.

---

## Trin 2 – Installer CodeQLs C#-analysepakke

Kontroller først, at CodeQL understøtter C#:

```powershell
C:\tools\codeql\codeql.exe resolve languages
```

Kontroller, at `csharp` er tilgængeligt.

Download derefter CodeQLs officielle C#-queries:

```powershell
C:\tools\codeql\codeql.exe pack download codeql/csharp-queries
```

Denne pakke indeholder de queries, som anvendes til at identificere forskellige typer af sikkerhedsproblemer i C#.

Når pakken er installeret, kan vi udføre statisk sikkerhedsanalyse af ASP.NET Core-projektet.

---

## Trin 3 – Konfigurer stier og projektmappe

For at gøre vejledningen uafhængig af projektets placering anvender vi PowerShell-variabler.

### Åbn projektet

Naviger til rodmappen for Order Management-projektet.

Det er den mappe, der indeholder solution-filen `OrderManagement.slnx`.

```powershell
cd "<sti-til-projekt>\OrderManagement"
```

Erstat `<sti-til-projekt>` med den faktiske placering på din computer.

Kontroller, at løsningen kan bygges:

```powershell
dotnet build
```

### Definer stier

Kør følgende kommandoer i PowerShell:

```powershell
$codeql = "C:\tools\codeql\codeql.exe"
$projectPath = (Get-Location).Path
$databasePath = Join-Path $env:TEMP "order-codeql-db"
$reportPath = Join-Path $env:TEMP "order-codeql-results.sarif"
```

Variablerne har følgende betydning:

| Variabel | Betydning |
|---|---|
| `$codeql` | Placeringen af CodeQL CLI |
| `$projectPath` | Rodmappen for Order Management-projektet |
| `$databasePath` | Placeringen af CodeQL-databasen |
| `$reportPath` | Placeringen af SARIF-resultatfilen |

Hvis CodeQL er installeret et andet sted, skal kun `$codeql` ændres.

Projektets placering findes automatisk ved hjælp af:

```powershell
(Get-Location).Path
```

Databasen og rapporten placeres i Windows' midlertidige mappe, så de ikke bliver tilføjet til projektets Git-repository.

---

## Trin 4 – Opret en CodeQL-database

CodeQL analyserer kildekoden ved først at oprette en særlig database, som indeholder en struktureret repræsentation af programmet.

Databasen anvendes efterfølgende til at udføre sikkerhedsrelaterede queries.

### Opret databasen

```powershell
& $codeql database create `
    $databasePath `
    --language=csharp `
    --source-root="$projectPath" `
    --command="dotnet build /t:rebuild"
```

### Forklaring

| Parameter | Betydning |
|---|---|
| `database create` | Opretter en ny CodeQL-database |
| `$databasePath` | Placering af analysedatabasen |
| `--language=csharp` | Angiver programmeringssproget |
| `--source-root="$projectPath"` | Angiver projektets rodmappe |
| `--command` | Angiver build-kommandoen |

Build-kommandoen anvender:

```powershell
dotnet build /t:rebuild
```

Dette sikrer, at projektet kompileres igen, så CodeQL kan indsamle de nødvendige oplysninger under build-processen.

### Forventet resultat

CodeQL opretter en analysedatabase i den mappe, som `$databasePath` peger på.

Databasen indeholder oplysninger, som CodeQL kan anvende til at analysere C#-koden.

> Bemærk: Databasemappen må ikke eksistere, inden kommandoen udføres. Hvis analysen skal gentages efter kodeændringer, skal databasen oprettes på ny.

---

## Trin 5 – Udfør statisk sikkerhedsanalyse

Når databasen er oprettet, kan vi udføre sikkerhedsanalysen.

Vi anvender CodeQLs `security-extended` query suite.

Denne indeholder både de almindelige sikkerhedsregler og yderligere regler, som kan identificere flere typer potentielle sikkerhedsproblemer.

### Kør analysen

```powershell
& $codeql database analyze `
    $databasePath `
    "codeql/csharp-queries:codeql-suites/csharp-security-extended.qls" `
    --format=sarif-latest `
    --output="$reportPath"
```

### Forklaring

| Parameter | Betydning |
|---|---|
| `database analyze` | Udfører CodeQL-analyse |
| `$databasePath` | Databasen, der skal analyseres |
| `csharp-security-extended.qls` | Den valgte samling af sikkerhedsregler |
| `--format=sarif-latest` | Angiver SARIF som outputformat |
| `--output="$reportPath"` | Placering af resultatfilen |

### Forventet resultat

Når analysen er gennemført, findes resultatfilen på den placering, som `$reportPath` angiver.

SARIF (Static Analysis Results Interchange Format) er et standardiseret JSON-baseret format til udveksling af resultater fra statiske analyseværktøjer.

Resultatfilen indeholder oplysninger om de sikkerhedsproblemer, CodeQL har identificeret.

---

## Trin 6 – Undersøg analyseresultaterne

Vi kan læse SARIF-rapporten direkte fra PowerShell.

### Indlæs rapporten

```powershell
$report = Get-Content $reportPath -Raw | ConvertFrom-Json
```

### Udskriv de identificerede fund

```powershell
$report.runs.results | ForEach-Object {
    [PSCustomObject]@{
        Rule = $_.ruleId
        Message = $_.message.text
        File = $_.locations[0].physicalLocation.artifactLocation.uri
        Line = $_.locations[0].physicalLocation.region.startLine
    }
} | Format-Table -AutoSize -Wrap
```

Denne kommando udskriver:

- CodeQL-reglens ID
- Beskrivelsen af fundet
- Den berørte kildekodefil
- Linjenummeret

Hvis der ikke rapporteres fund, vil tabellen være tom.

### Alternativ visning

For at få en mere læsevenlig visning kan følgende anvendes:

```powershell
$report.runs.results | ForEach-Object {
    [PSCustomObject]@{
        Rule = $_.ruleId
        File = $_.locations[0].physicalLocation.artifactLocation.uri
        Line = $_.locations[0].physicalLocation.region.startLine
        Message = $_.message.text
    }
} | Format-List
```

---

## Trin 7 – Forventede sikkerhedsfund

Ved den gennemførte analyse af Order Management API'et blev følgende to sårbarheder identificeret.

### Fund 1 – SQL Injection

**CodeQL-regel:**

`cs/sql-injection`

**Fil:**

`OrderManagement.Api/Controllers/OrdersController.cs`

**Rapporteret besked:**

`This query depends on this ASP.NET Core MVC action method parameter.`

Fundet skyldes, at et inputparameter fra et HTTP-request anvendes direkte til at konstruere en SQL-forespørgsel.

Den relevante kode er:

```csharp
var sql =
    $"SELECT * FROM Orders WHERE CustomerName = '{customerName}'";

var results = await db.Orders
    .FromSqlRaw(sql)
    .ToListAsync();
```

Her kan brugerinput ændre SQL-forespørgslens logik.

Sårbarheden er tidligere blevet verificeret dynamisk gennem en HTTP-request med en SQL Injection-payload.

### Fund 2 – Path Traversal

**CodeQL-regel:**

`cs/path-injection`

**Fil:**

`OrderManagement.Api/Controllers/OrdersController.cs`

**Rapporteret besked:**

`This path depends on a user-provided value.`

Fundet skyldes, at et filnavn fra et HTTP-request anvendes direkte til at konstruere en filsti.

Den relevante kode er:

```csharp
var path = Path.Combine(
    environment.ContentRootPath,
    "Documents",
    fileName);

var content = await System.IO.File.ReadAllTextAsync(path);
```

Hvis klienten kan påvirke stien ved hjælp af eksempelvis `../`, kan applikationen potentielt læse filer uden for den tilsigtede dokumentmappe.

---

## Trin 8 – Information Disclosure

Vi har tidligere verificeret en Information Disclosure-sårbarhed gennem en HTTP-request, som fremprovokerede en exception.

Applikationen returnerede blandt andet:

- Interne filstier
- Klassenavne
- Metodenavne
- Kildekodefil og linjenummer
- Stack trace

Sårbarheden skyldes følgende kode:

```csharp
catch (Exception ex)
{
    return StatusCode(500, ex.ToString());
}
```

Vi ønskede at undersøge, om CodeQL kunne identificere denne sårbarhed gennem reglen:

`cs/information-exposure-through-exception`

### Resultat

**Information Disclosure blev ikke rapporteret ved den gennemførte CodeQL-analyse.**

Det betyder ikke, at sårbarheden ikke eksisterer.

Vi har allerede demonstreret, at applikationen returnerer interne oplysninger til klienten.

Det viser en væsentlig begrænsning ved automatiseret statisk kodeanalyse.

En CodeQL-analyse kan gennemføres succesfuldt uden nødvendigvis at identificere alle eksisterende sikkerhedssårbarheder.

Det er derfor vigtigt at kombinere statisk analyse med andre former for sikkerhedstest og manuel vurdering.

---

## Trin 9 – Gentag analysen efter kodeændringer

Hvis kildekoden ændres, skal CodeQL-databasen oprettes igen, så analysen undersøger den opdaterede kode.

Stop eventuelle analyser, der anvender databasen.

### Slet den eksisterende database

```powershell
if (Test-Path $databasePath) {
    Remove-Item $databasePath -Recurse -Force
}
```

Kommandoen fjerner kun den eksisterende CodeQL-database på den angivne placering.

### Opret databasen igen

```powershell
& $codeql database create `
    $databasePath `
    --language=csharp `
    --source-root="$projectPath" `
    --command="dotnet build /t:rebuild"
```

### Kør sikkerhedsanalysen igen

```powershell
if (Test-Path $reportPath) {
    Remove-Item $reportPath -Force
}

& $codeql database analyze `
    $databasePath `
    "codeql/csharp-queries:codeql-suites/csharp-security-extended.qls" `
    --format=sarif-latest `
    --output="$reportPath"
```

Indlæs den opdaterede rapport:

```powershell
$report = Get-Content $reportPath -Raw | ConvertFrom-Json
```

Undersøg derefter, om de tidligere identificerede sikkerhedsfund stadig optræder i rapporten.

Når sårbarhederne senere rettes, skal dette trin anvendes til at verificere, at CodeQL ikke længere rapporterer de samme fund.

---

## Verifikation

**Status: VERIFICERET – LOKAL CODEQL-ANALYSE**

Følgende resultater blev observeret:

| Sårbarhed | CodeQL-regel | Resultat |
|---|---|---|
| SQL Injection | `cs/sql-injection` | Identificeret |
| Path Traversal | `cs/path-injection` | Identificeret |
| Information Disclosure | `cs/information-exposure-through-exception` | Ikke rapporteret |

De to identificerede fund blev rapporteret i:

`OrderManagement.Api/Controllers/OrdersController.cs`

CodeQL-analysen blev udført lokalt med CodeQL CLI og `security-extended`-regelsættet.

### Hvad er verificeret?

- CodeQL kunne oprette en analysedatabase for C#-projektet.
- Den statiske analyse kunne gennemføres.
- SARIF-resultatfilen blev genereret.
- SQL Injection blev identificeret.
- Path Traversal blev identificeret.
- Information Disclosure blev ikke identificeret i den gennemførte analyse.

### Hvad mangler?

Det skal fortsat verificeres, at de to CodeQL-fund forsvinder efter rettelse af kildekoden.

Det skal også kontrolleres, at samme forventede fund rapporteres, når CodeQL senere integreres i GitHub Actions.

---

## Faglig opsamling

Denne test demonstrerer forskellen mellem at udføre en sikkerhedsanalyse og at verificere, at applikationen er sikker.

CodeQL kan identificere potentielle sikkerhedsproblemer ved blandt andet at analysere dataflows fra usikre kilder til sikkerhedskritiske operationer.

Men en gennemført analyse betyder ikke, at alle sårbarheder er identificeret.

Vi har i vores applikation demonstreret dette ved, at:

1. SQL Injection blev identificeret af CodeQL.
2. Path Traversal blev identificeret af CodeQL.
3. Information Disclosure kunne demonstreres dynamisk, men blev ikke rapporteret af CodeQL.

Det understreger vigtigheden af at kombinere:

- Statisk kodeanalyse (SAST)
- Dynamisk sikkerhedstest
- Unit tests
- Manuel kodegennemgang
- Risikobaserede sikkerhedsvurderinger

Denne tilgang understøtter princippet om **defense in depth**.

### Relevante kilder

- GitHub CodeQL CLI:
  https://github.com/github/codeql-cli-binaries/releases
- GitHub CodeQL-dokumentation:
  https://docs.github.com/en/code-security/codeql-cli
- CodeQL C# Query Help:
  https://codeql.github.com/codeql-query-help/csharp/
- Secure by Design, kapitel 8 – Leveraging your delivery pipeline for security.

---


# Samlet testoversigt

| Sårbarhed | Testmetode | Status |
|---|---|---|
| SQL Injection | HTTP-request med SQL-payload | Verificeret |
| Path Traversal | HTTP-request med `../Program.cs` | Afventer |
| Information Disclosure | HTTP-request der fremprovokerer exception | Afventer |
| Sårbar NuGet-pakke | NuGet Audit | Afventer |
| CodeQL SQL Injection | Statisk kodeanalyse | Afventer |
| CodeQL Path Traversal | Statisk kodeanalyse | Afventer |
| CodeQL Information Disclosure | Statisk kodeanalyse | Afventer |

---

# Faglig opsamling

De forskellige testmetoder undersøger forskellige egenskaber ved applikationen.

**Dynamisk sikkerhedstest**

- Undersøger den eksekverende applikation.
- Kan demonstrere konkret udnyttelse af en sårbarhed.
- Kræver, at relevante input og angrebsveje afprøves.

**Statisk sikkerhedsanalyse (SAST)**

- Analyserer kildekoden uden at eksekvere applikationen.
- Kan identificere usikre kodekonstruktioner og dataflows.
- Kan give falske positiver og overse reelle sårbarheder.

**Software Composition Analysis (SCA)**

- Undersøger anvendte tredjepartsafhængigheder.
- Identificerer kendte sikkerhedsadvarsler for afhængigheder.
- Dokumenterer ikke nødvendigvis, at en sårbarhed kan udnyttes i den konkrete applikation.

## Konklusion

Et succesfuldt build og fem beståede unit tests er ikke ensbetydende med, at applikationen er sikker.

Tilsvarende er en succesfuld CodeQL-analyse ikke en garanti for fravær af sikkerhedssårbarheder.

Det er derfor nødvendigt at kombinere forskellige former for test, automatiseret sikkerhedsanalyse og manuel vurdering.

Dette understøtter princippet om **defense in depth** og arbejdet med sikkerhed gennem hele softwareudviklingsprocessen.
