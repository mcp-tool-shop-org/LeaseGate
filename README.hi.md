<p align="center">
  <a href="README.ja.md">日本語</a> | <a href="README.zh.md">中文</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.hi.md">हिन्दी</a> | <a href="README.it.md">Italiano</a> | <a href="README.pt-BR.md">Português (BR)</a>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/mcp-tool-shop-org/LeaseGate/main/assets/logo-leasegate.png" alt="LeaseGate" width="400">
</p>

<p align="center">
  <a href="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml"><img src="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow" alt="MIT License"></a>
  <a href="https://mcp-tool-shop-org.github.io/LeaseGate/"><img src="https://img.shields.io/badge/Landing_Page-live-blue" alt="Landing Page"></a>
</p>

**स्थानीय-प्रथम एआई गवर्नेंस नियंत्रण प्रणाली जो निष्पादन पट्टे जारी करती है, नीतियों और बजटों को लागू करती है, और छेड़छाड़-रोधी गवर्नेंस प्रमाण उत्पन्न करती है।**

---

## एक नज़र में

- **पट्टे का आवंटन:** मल्टी-पूल गवर्नेंस के साथ TTL-आधारित निष्पादन पट्टे।
- **नीति प्रवर्तन:** हस्ताक्षरित बंडल स्टेज/एक्टिवेट प्रवाह के साथ GitOps YAML नीतियां।
- **छेड़छाड़-रोधी ऑडिट:** हैश-चेन किए गए, केवल-जोड़ने योग्य प्रविष्टियाँ और रिलीज़ रसीदें।
- **सुरक्षा स्वचालन:** कूलडाउन, क्लैंप और सर्किट-ब्रेकर पैटर्न।
- **उपकरण अलगाव:** कमांड इंजेक्शन रोकथाम के साथ नियंत्रित उप-पट्टे।
- **वितरित मोड:** हब/एजेंट आर्किटेक्चर जिसमें स्थानीय स्तर पर बैकअप सुविधा है।

---

## वर्तमान स्थिति — v0.1.0

चरण 1-5 को लागू किया गया है, परीक्षण किया गया है और सुरक्षा के लिए मजबूत किया गया है, जिसमें शामिल हैं:

- पट्टे का आवंटन और TTL-आधारित रिलीज़ सुरक्षा।
- मल्टी-पूल गवर्नेंस (समवर्तीता, दर, संदर्भ, कंप्यूट, व्यय)।
- पुनरारंभ रिकवरी के साथ टिकाऊ SQLite स्थिति।
- हैश-चेन किए गए ऑडिट प्रविष्टियाँ और रिलीज़ रसीदें।
- हस्ताक्षरित नीति बंडल स्टेज/एक्टिवेट प्रवाह।
- नियंत्रित उप-पट्टों के साथ उपकरण अलगाव।
- हब/एजेंट वितरित मोड जिसमें स्थानीय स्तर पर बैकअप व्यवहार है।
- आरबीएसी, सेवा खाते, पदानुक्रमित कोटा, निष्पक्षता नियंत्रण।
- समीक्षक ट्रेल्स के साथ अनुमोदन कतार।
- नियतात्मक बैकअप योजनाओं के साथ इरादा रूटिंग।
- नियंत्रित सारांश ट्रेसेस के साथ संदर्भ गवर्नेंस।
- सुरक्षा स्वचालन (कूलडाउन, क्लैंप, सर्किट-ब्रेकर)।
- गवर्नेंस प्रमाण निर्यात और सत्यापन।

### v0.1.0 सुरक्षा सुदृढ़ीकरण

- उपकरण अलगाव में कमांड इंजेक्शन रोकथाम (शेल मेटाकैरेक्टर ब्लैकलिस्ट + प्रत्यक्ष निष्पादन)।
- सेवा खाते टोकन हैशिंग (SHA-256 जिसमें प्लेनटेक्स्ट संगतता है)।
- विफलता ट्रैकिंग के साथ लचीली ऑडिट लेखन (अब कोई "अंधा" और "भूलने योग्य" लेखन नहीं)।
- पाइप संदेश फ़्रेमिंग पर पेलोड आकार सीमा (16 एमबी)।
- थ्रेड-सुरक्षित रजिस्ट्री और क्लाइंट स्थिति (पूरे में ConcurrentDictionary)।
- समवर्ती नामित पाइप कनेक्शन (ब्लॉकिंग लिसनर के बिना डिस्पैच)।
- सभी निर्यात एंडपॉइंट पर पथ ट्रैवर्सल सुरक्षा।
- रिपोर्ट निर्यात में सीएसवी फॉर्मूला इंजेक्शन रोकथाम।
- सुरक्षा स्वचालन स्थिति पर असीमित विकास सीमा।
- नीति पुनः लोड त्रुटि ट्रैकिंग और एक्सपोज़र।
- गवर्नेंस रसीद हस्ताक्षर के लिए बाहरी कुंजी समर्थन।

---

## समाधान लेआउट

```text
LeaseGate.sln
src/
  LeaseGate.Protocol/     # DTOs, enums, serializer + framing
  LeaseGate.Policy/       # policy model, evaluator, GitOps loader
  LeaseGate.Audit/        # append-only hash-chained audit writer
  LeaseGate.Service/      # governor, pools, approvals, safety, tool isolation
  LeaseGate.Client/       # SDK commands + governed call wrapper
  LeaseGate.Providers/    # provider interface + adapters
  LeaseGate.Storage/      # durable SQLite-backed state
  LeaseGate.Hub/          # distributed quota and attribution control plane
  LeaseGate.Agent/        # hub-aware agent with local degraded fallback
  LeaseGate.Receipt/      # proof export + verification services
samples/
  LeaseGate.SampleCli/    # end-to-end scenarios and proof/report commands
  LeaseGate.AuditVerifier/# audit chain verification sample
tests/
  LeaseGate.Tests/        # unit/integration coverage through phase 5
policies/
  org.yml
  models.yml
  tools.yml
  workspaces/*.yml
```

---

## शुरुआत कैसे करें

### बिल्ड और परीक्षण

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### नमूना परिदृश्य चलाएं

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### ऑपरेशनल कमांड

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## एकीकरण स्नैपशॉट

एक विशिष्ट एप्लिकेशन प्रवाह:

1. एक `AcquireLeaseRequest` बनाएं जिसमें अभिनेता, संगठन/कार्यक्षेत्र, इरादा, मॉडल, अनुमानित उपयोग, उपकरण और संदर्भ योगदान शामिल हों।
2. `LeaseGateClient.AcquireAsync(...)` के माध्यम से प्राप्त करें।
3. मॉडल/उपकरण कार्य निष्पादित करें (या `GovernedModelCall.ExecuteProviderCallAsync(...)` का उपयोग करें)।
4. वास्तविक टेलीमेट्री और परिणामों के साथ `LeaseGateClient.ReleaseAsync(...)` के माध्यम से रिलीज़ करें।
5. आवश्यकता पड़ने पर रसीद प्रमाणों को सहेजें/सत्यापित करें।

[docs/Protocol.md](docs/Protocol.md) और [docs/Architecture.md](docs/Architecture.md) देखें।

---

## GitOps नीति कार्यप्रवाह

नीति स्रोत `policies/` में स्थित है और GitOps YAML संरचना के माध्यम से लोड किया जाता है।

- साझा डिफ़ॉल्ट और वैश्विक थ्रेसहोल्ड के लिए `org.yml`।
- मॉडल अनुमति सूचियों और कार्यक्षेत्र मॉडल ओवरराइड के लिए `models.yml`।
- अस्वीकृत/अनुमोदन-आवश्यक श्रेणियों और समीक्षक आवश्यकताओं के लिए `tools.yml`।
- कार्यक्षेत्र-स्तरीय बजट और भूमिका क्षमता मानचित्रों के लिए `workspaces/*.yml`।

CI सत्यापन और बंडल हस्ताक्षर निम्नलिखित द्वारा प्रदान किए जाते हैं:

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## प्रलेखन

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## लाइसेंस

[MIT](LICENSE)
