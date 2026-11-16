# LAB: بيانات اختبار السايكل الحالي

المالك: مروى. بيانات LAB جديدة ومنفصلة عن بيانات DEMO القديمة. لم تُحذف بيانات أو حسابات قديمة، وخدمة Audit وسجلاتها محفوظة. ملفات صفحات Sourcing وManager وTop Committee لم تتغير.

## تجهيز أي جهاز في الفريق

1. اسحبوا التعديلات من dev، وتأكدوا أن migrations وديع مطبقة، خصوصًا EvaluationCommitteeSubmissions وFinancialEvaluations.CompletedAt.
2. استخدموا ملف .env وقاعدة SQL الخاصة بجهازكم. الإعدادات المحلية:

```dotenv
SEED_TEST_USERS=true
SEED_DEMO_DATA=true
SEED_DEMO_RUN=01
```

3. قيمة SEED_TEST_PASSWORD في .env هي باسورد الحسابات الجديدة. لا تحفظوا الباسوورد في Git. الحساب القديم يحتفظ بباسوورده؛ الـseed لا يغيّره.
4. شغلوا المشروع في Development. Program يستدعي DemoDataSeeder الموجود أصلًا، وهو يضيف مجموعة LAB الناقصة تلقائيًا.
5. الحسابات الجديدة تدخل من شاشة الدخول العادية بكتابة الإيميل. أكملوا الإعداد الأمني/البصمة عند طلبه. لا توجد بصمات أو ملفات موردين مزوّرة ولا تجاوز للـonboarding.

لا تحتاج هذه الإضافة migration جديدة؛ هي بيانات فقط، بعد تطبيق migrations الخاصة بميزة التوصية.

## ابدئي من هذا التندر

**LAB-01-EVL** هو السيناريو الرئيسي: 3 عروض Compliant، 6 أسئلة فنية مجموعها 80، مالي 20، و4 أعضاء معيّنون. يبدأ بدون درجات، أو FinancialEvaluations، أو Ready، أو أصوات، أو تقرير.

| المهمة | الحساب |
| --- | --- |
| عضو Department — Evaluation Committee | lab.department@demo.local |
| عضو Financial | lab.financial@demo.local |
| عضو Technical | lab.technical@demo.local |
| عضو اللجنة الرابع + صلاحية Manager لاحقًا | lab.manager@demo.local |
| Sourcing Officer | lab.officer@demo.local |
| الموردون | lab.supplier1@demo.local إلى lab.supplier4@demo.local |

الحسابات تستخدم JobTitle يبدأ بـLAB، حتى لا تُضاف نسخ ثانية من المناصب النصية التي يستخدمها Sourcing للتعيين التلقائي. تعيين لجنة تندرات LAB محفوظ صراحةً بـUserId، وليس بالبحث عن هذه المسميات.

## خطوات التجربة اليدوية

1. ادخلي بكل واحد من الأعضاء الأربعة وافتحي Evaluation ثم LAB-01-EVL. الحسابات الأربعة تحمل EvaluationCommittee وتفتح واجهة التقييم عند الدخول. بعد تحديث أدوار حساب موجود، اعملي Logout ثم Login حتى تتجدد صلاحيات الجلسة.
2. افتحي كل عرض، جرّبي Save Draft، ثم عبّي الأسئلة الستة واضغطي Save changes لكل عرض. Draft لا يكفي لتفعيل Ready.
3. ارجعي إلى All offers. يتفعل **Ready واحد للعطاء كله** بعد حفظ كل عروضك. تأكيده يقفل درجاتك وملاحظاتك في كل العروض.
4. بعد Ready للأعضاء الأربعة، تتحدث الصفحة دوريًا ويظهر جدول النقاط وقائمة Recommended offer. لا يوجد تقرير مرسل حتى الآن.
5. كل عضو يختار عرضًا ويضغط Confirm recommendation. لا يكفي أول صوت أو أول ثلاثة أصوات.
6. جرّبي 2–1–1: صاحب الصوتين يفوز. في 2–2 نفاضل بين العرضين المتعادلين بمجموع اللجنة الفني + المالي. إذا الأصوات والمجموع النهائي متساويان، تبدأ جولة أخرى وتبقى الدرجات مقفلة.
7. عند حسم التوصية، يُنشأ EvaluationReport واحد وفيه RecommendedBidId غير فارغ. يُرسل إشعار واحد إلى lab.officer@demo.local ورابطه Evaluation tracking.
8. ادخلي بحساب الـOfficer: افتحي Evaluation tracking وابحثي عن LAB-01-EVL، اقرئي التقرير واضغطي Send to Manager الموجود ضمن شغل ضياء.
9. ادخلي بحساب lab.manager@demo.local: افتحي إشعار Evaluation report ready for review المرسل من الـOfficer، أو افتحي /Manager/Reports مباشرةً، ثم التندر. دخوله الافتراضي يبقى Evaluation لكنه يحمل صلاحية SourcingManager أيضًا، لأن مراجعة الـManager مرتبطة بمقعده في هذا التندر. يظهر المورد الموصى به، وبذلك تتوفر البيانات اللازمة لتفعيل Approve. اكتبي الملاحظة وأرسلي الموافقة.
10. LAB-01-EVL مُعدّ في الـseed بـRequiresTopCommittee=true، لذلك يذهب للـTop Committee بعد الموافقة. تصويتهم وإجراء القرار من شغل أصحاب الصفحات، لم نعدله.

الاختبار الآلي نفّذ خطوات الـcontrollers والخدمات الحقيقية على قاعدة منفصلة. البيانات الموجودة في قاعدة مروى تُركت فاضية لتجربتها يدويًا؛ لم تُستهلك في الاختبار.

## باقي سيناريوهات المجموعة

| التندر / الطلب | ماذا تختبرون؟ |
| --- | --- |
| LAB-01-EVL | السايكل من تقييمات فاضية حتى الـOfficer والـManager ثم Top Committee. 3 عروض. |
| LAB-01-DIRECT | نفس بداية التقييم، لكن RequiresTopCommittee=false لاختبار الترسية المباشرة بعد موافقة الـManager. |
| LAB-01-TIE | 4 عروض بنفس السعر. أعطوا نفس الدرجات لكل العروض من كل عضو، ثم صوتًا لكل عرض: يجب أن تفتح جولة جديدة. بعدها اختاروا 3–1 لتكمل التوصية. |
| LAB-01-FLOW | 3 عروض Submitted بلا Compliance ولا إشعارات لجنة. الـOfficer يراجع المطابقة ثم يرسل للجنة من المسار الموجود عند ندى. |
| LAB-01-SUP | مناقصة Published مع 4 دعوات Pending وبلا عروض. الموردون يقبلون الدعوات ويقدمون عروضهم من صفحاتهم الحقيقية. لا توجد مرفقات مصطنعة. |
| LAB-01-PR-DRAFT | طلب تجريبي منشأ بالـseed؛ لا يلزم لبدء اختبار التقييم. |
| LAB-01-PR-SENT | طلب Submitted ليتابعه الـOfficer. |
| LAB-01-PR-OK | طلب Approved لبدء إنشاء مناقصة من Sourcing بالطريقة العادية. تعيين هذه المناقصة الجديدة يخضع لإعداد Sourcing الحالي. |

## Top Committee

الخدمة الحالية تشترط أربعة حسابات مؤهلة بالضبط: TopCommittee فقط، موظف نشط وغير مقفول. الـseed يعيد استخدام المؤهلين الموجودين ويضيف العدد الناقص فقط، ولا يعطّل أو يغيّر حسابًا قديمًا.

في قاعدة مروى عند تجهيز LAB-01، الأربعة هم:
- top@lumiere.local
- lab.top1@demo.local
- lab.top2@demo.local
- lab.top3@demo.local

قد تختلف الأسماء على جهاز آخر. إذا كان عندهم أكثر من أربعة حسابات مؤهلة أصلًا، يلزم مراجعة الإعداد مع مسؤول صفحة Top Committee؛ لا نحذف حسابات لتجاوز هذه القاعدة.

## بدك تعيدي التجربة من الصفر؟

غيّري فقط الإعداد المحلي إلى SEED_DEMO_RUN=02 وأعيدي تشغيل المشروع. تنضاف LAB-02-EVL وبقية المجموعة فاضية، وتُستخدم نفس حسابات LAB. لا يلزم إعادة إعداد حسابات جديدة، ولا ينحذف LAB-01 أو الـAudit.

نفس رقم المجموعة لا يُعاد تصفيره عند restart أو pull؛ هذا مقصود حتى لا يضيع شغلكم. يمكن استخدام 03 أو اسم قصير من 1–8 أحرف/أرقام لمجموعة أخرى.

## التأكد من قاعدة البيانات وAudit

- قبل التقييم: لا يوجد صف في EvaluationReports لهذا التندر، ولا EvaluationCommitteeSubmissions.
- بعد Save changes: تُحفظ TechnicalEvaluations وFinancialEvaluations.CompletedAt، والتقييم يظل قابلًا للتعديل.
- بعد Ready: صف واحد في EvaluationCommitteeSubmissions لكل عضو، وتُقفل FinancialEvaluations.ReadyAt لكل عروضه معًا.
- بعد التصويت: RecommendedBidId وVotedAt وVotingRound في EvaluationCommitteeSubmissions.
- بعد الحسم: EvaluationReports.RecommendedBidId هو العرض النهائي، وSubmittedAt موجود.
- تُسجّل DemoAccountCreated وDemoTestingRunCreated، ثم EvaluationReady وRecommendationVoted وSubmittedToOfficer وSubmittedToManagerReview وManagerDecision وخطوات Top Committee من خدماتهم الموجودة.

## التحقق المنفذ

- 94 فحصًا على SQL منفصلة: بداية فاضية، قواعد اللجنة والأوزان، idempotence، الحفاظ على Audit، السايكل الفعلي حتى موافقة Manager وتصويت الأربعة في Top Committee والترسية، ومسار Manager المباشر.
- 29 فحصًا على قاعدة مروى بعد إضافة LAB-01: migrations مطبقة، 5 تندرات جديدة بلا تقييمات/Ready/تصويت/تقارير، 3 طلبات تجريبية، وAudit القديم محفوظ كما هو.
- البناء نجح. ظهر تحذير nullable موجود في Services/Ai/AiExcelTemplates.cs:204، خارج نطاق الـseed؛ لم يُعدل.
- لم نختبر يدويًا تسجيل الدخول والبصمة أو رفع مستندات الموردين لهذه الحسابات الجديدة. هذه تتم بالطريقة الطبيعية على جهاز المستخدم.
تحديث الأدوار: lab.department أصبح EvaluationCommittee فقط. lab.financial وlab.technical يحملان EvaluationCommittee. lab.manager يحمل EvaluationCommittee وSourcingManager، ودخوله الافتراضي Evaluation. التعديل يقتصر على حسابات LAB المعروفة، ويحفظ UserId والباسوورد والتعيينات والدرجات. تغيير الأدوار يسجّل DemoRolesUpdated في Audit. لم يُعدل DashboardRouter أو أي صفحة/Sidebar مشتركة.

انتقال حساب LAB إلى Manager: قبل إنشاء تقرير LAB-<run>-EVL، يحمل lab.manager دور التقييم أيضًا لبدء التجربة مع اللجنة. بعد إنشاء تقرير هذا التندر، إعادة تشغيل المشروع بنفس رقم المجموعة تجعل الـseed يُبقي SourcingManager فقط لهذا الحساب، فيفتح Manager Dashboard عند الدخول. سجلي الخروج والدخول لتجديد الجلسة. في قاعدة مروى تم تطبيق هذا الانتقال بعد اكتمال LAB-01-EVL، والتحقق أن الدرجات والأصوات والتقرير والتعيينات لم تتغير. تغيير رقم المجموعة إلى مجموعة جديدة يعيد دور التقييم لهذا الحساب لبدء تجربة جديدة. لم تتغير صفحة الدخول أو التوجيه المشترك.
