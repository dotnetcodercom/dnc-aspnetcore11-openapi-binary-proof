# تعليمات اختبار Demo على Windows

هذه حزمة اختبار محلية مستقلة. تحتاج إلى .NET SDK 11.0.100-rc.1.26425.128، وهو إصدار إعادة الإنتاج المثبت. لا تحتاج Visual Studio أو Node أو Docker أو SQL Server أو حساب Azure.

## 1. تشغيل الاختبار في المتصفح

فك ضغط ZIP داخل Downloads. افتح المجلد الذي يحتوي على Start-Demo.ps1، ثم افتح PowerShell في ذلك المجلد وشغّل:

```powershell
Get-ChildItem -Recurse -Filter *.ps1 | Unblock-File
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-Demo.ps1
```

انتظر ظهور BROWSER، ثم افتح http://127.0.0.1:5129 في Chrome أو Edge. أبق نافذة PowerShell مفتوحة. اضغط Run verification مرة واحدة وانتظر النتيجة. الصفحة تعرض نتائج فعلية من التطبيق؛ ليست نتيجة توضيحية جاهزة. عند إعادة تشغيل الخادم يبدأ العرض بحالة NOT RUN.

النتيجة المتوقعة هي PASS · 12/12 checks. إذا ظهر FAIL أو ERROR، احتفظ بالنتيجة والسجل واحتفظ بهما كما هما؛ لا تعتبر ظهور الصفحة نجاحاً للاختبار.

إذا كان المنفذ مستخدماً، جرّب:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-Demo.ps1 -Port 5139
```

وافتح http://127.0.0.1:5139 بدلاً من العنوان الأول. اضغط Ctrl+C في PowerShell لإيقاف التطبيق.

## 2. حفظ الدليل والصور

بعد النتيجة النهائية، استخدم Win+Shift+S لالتقاط صورتين حقيقيتين من المتصفح: الأولى تشمل عنوان Demo وإصدار .NET والنتيجة؛ والثانية تشمل الفحوص المهمة. يمكن تصغير العرض قليلاً أو تمرير الصفحة. لا تقص النتيجة أو اسم الفحص بصورة تغير معناهما.

اضغط Download receipt لتنزيل JSON. يحفظ التطبيق أيضاً receipt.json داخل evidence/مجلد-التشغيل. ويتضمن إصدار Runtime وASP.NET Core، الوقت، نتائج كل فحص، البيانات المقاسة وبصمات ملفات المصدر. احتفظ بملف receipt.json مع المصدر؛ الصور وحدها لا تثبت كل الحالات التقنية.

إذا أردت سجل PowerShell كاملاً، شغّل أيضاً:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Demo.ps1
```

هذا الاختبار لا يحتاج المتصفح، ويشغّل خادماً مؤقتاً ثم يوقفه. يحفظ console.txt في evidence/console-... وreceipt.json في مجلد تشغيل مستقل. قد ترى NETSDK1057 لأنه إصدار Preview؛ افحص النتيجة النهائية. لا تُرسل bin أو obj؛ يمكن ضغط evidence فقط عند مشاركة الدليل.

## عند غياب SDK

شغّل dotnet --list-sdks. يجب أن يظهر 11.0.100-rc.1.26425.128. يمكنك تنزيل .NET 11 RC1 SDK من المصدر الرسمي: https://dotnet.microsoft.com/download/dotnet/11.0 . اختر SDK المناسب لجهاز Windows، لا Runtime وحده. الحزمة لا تثبت SDK أو تغير إعدادات جهازك تلقائياً. إذا كان لديك إصدار .NET 11 مختلف، وثّق رقم الإصدار قبل تغيير global.json؛ إعادة الاختبار على إصدار آخر تحتاج توثيقاً جديداً.

## ما يثبته هذا Demo

يفحص FileContentHttpResult وFileStreamHttpResult في Minimal APIs، وFileContentResult وFileStreamResult في Controllers. يقارن schema الفعلي في OpenAPI مع HTTP status وContent-Type واسم الملف وbytes وSHA-256. يثبت أيضاً اكتشاف metadata خاطئة عمداً، واستجابة Range محددة بـ206.

الإصدارات مثبتة: Microsoft.AspNetCore.OpenApi 11.0.0-rc.1.26425.128 وMicrosoft.OpenApi 3.10.0. أول تشغيل يحتاج إنترنت لاستعادة هاتين الحزمتين من NuGet. لا تحتاج تنزيلهما يدوياً.

يفرض OpenApi3_0 ويصدر serializer هنا 3.0.4. افتح /openapi/v1.json لقراءة الملف الحقيقي. تحفظ نسخة openapi.json مع receipt.json في evidence. لا يعدّل Demo أي schema عبر transformer لإجبار النجاح.

هذا الاختبار يثبت generated schema وعميل HTTP يقرأ bytes. لا يشغّل NSwag أو Kiota أو مولد TypeScript، ولا يقارن .NET 10 مع 11؛ فلا نستخدمه لادعاء نجاح كل client generator أو إثبات فرق كل إصدار.

المصدر الرسمي: https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11?view=aspnetcore-11.0

