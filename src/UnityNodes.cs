using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    internal static class UnityNodes
    {
        public const string Cat = "Unity / Объекты";

        public static void Register(NodeLibrary lib)
        {
            // ---------- Поиск объектов ----------

            var find = Def.Make("unity.findgo", Cat, "Find GameObject", "GameObject.Find по имени")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "PLAYER")
                .AddOut("out", "Объект", PortKind.GameObject);
            find.Emit = (c, n) => $"GameObject.Find({CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "PLAYER"))})";
            lib.Register(find);

            var findIns = Def.Make("unity.findwithtag", Cat, "Find With Tag", "GameObject.FindWithTag")
                .EnumIn("tag", "Тег", new[] { "Player", "Car", "NPC", "Road", "Item", "Trigger", "Untagged" }, "Player")
                .AddOut("out", "Объект", PortKind.GameObject);
            findIns.Emit = (c, n) =>
            {
                var tag = Def.Key(n, "tag", c.NodeDef(n), "Player");
                return $"GameObject.FindWithTag(\"{tag}\")";
            };
            lib.Register(findIns);

            var findChild = Def.Make("unity.findchild", Cat, "Find Child", "Найти потомка по пути")
                .AddIn("parent", "Родитель", PortKind.GameObject, literal: false)
                .AddIn("path", "Путь", PortKind.String, literal: true, def_: "child")
                .AddOut("out", "Объект", PortKind.GameObject);
            findChild.Emit = (c, n) =>
            {
                var p = c.ResolveInput(n, c.Port(n, "parent"));
                return $"({p} != null ? {p}.transform.Find({CodegenContext.Quote(Def.Key(n, "path", c.NodeDef(n), ""))}) : null)";
            };
            lib.Register(findChild);

            var getComp = Def.Make("unity.getcomponent", Cat, "Get Component", "GetComponent<T>()")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .EnumIn("type", "Тип компонента", new[]
                {
                    "Transform", "Rigidbody", "Collider", "BoxCollider", "SphereCollider",
                    "MeshRenderer", "MeshFilter", "Renderer", "Material", "AudioSource",
                    "Light", "Camera", "Animator", "WheelCollider", "TrailRenderer", "ParticleSystem"
                }, "Transform")
                .AddOut("out", "Компонент", PortKind.Component);
            getComp.Emit = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var t = Def.Key(n, "type", c.NodeDef(n), "Transform");
                var tc = t switch
                {
                    "BoxCollider" => "BoxCollider",
                    "SphereCollider" => "SphereCollider",
                    "MeshRenderer" => "MeshRenderer",
                    "MeshFilter" => "MeshFilter",
                    "AudioSource" => "AudioSource",
                    "WheelCollider" => "WheelCollider",
                    "TrailRenderer" => "TrailRenderer",
                    "ParticleSystem" => "ParticleSystem",
                    _ => t
                };
                return $"({go} != null ? {go}.GetComponent<{tc}>() : null)";
            };
            lib.Register(getComp);

            var getCompRigidbody = Def.Make("unity.getrb", Cat, "Get Rigidbody", "GetComponent<Rigidbody>()")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Rigidbody", PortKind.Rigidbody);
            getCompRigidbody.Emit = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                return $"({go} != null ? {go}.GetComponent<Rigidbody>() : null)";
            };
            lib.Register(getCompRigidbody);

            var getCompTransform = Def.Make("unity.gettransform", Cat, "Get Transform", ".transform")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Transform", PortKind.Transform);
            getCompTransform.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "go"))} != null ? {c.ResolveInput(n, c.Port(n, "go"))}.transform : null)";
            lib.Register(getCompTransform);

            var active = Def.Make("unity.isactive", Cat, "Is Active", "Активен ли объект")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Активен", PortKind.Bool);
            active.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "go"))} != null && {c.ResolveInput(n, c.Port(n, "go"))}.activeSelf)";
            lib.Register(active);

            var getName = Def.Make("unity.getname", Cat, "Get GameObject Name", "Имя объекта")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Имя", PortKind.String);
            getName.Emit = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                return $"({go} != null ? {go}.name : \"\")";
            };
            lib.Register(getName);

            var getTag = Def.Make("unity.gettag", Cat, "Get Tag", "Тег объекта")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Тег", PortKind.String);
            getTag.Emit = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                return $"({go} != null ? {go}.tag : \"\")";
            };
            lib.Register(getTag);

            // ---------- Трансформ ----------

            var getPos = Def.Make("unity.getposition", Cat, "Get Position", ".position")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Позиция", PortKind.Vector3);
            getPos.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".position";
            lib.Register(getPos);

            var getLocalPos = Def.Make("unity.getlocalposition", Cat, "Get Local Position", ".localPosition")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Позиция", PortKind.Vector3);
            getLocalPos.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".localPosition";
            lib.Register(getLocalPos);

            var getRot = Def.Make("unity.getrotation", Cat, "Get Rotation", ".rotation (Euler)")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Углы", PortKind.Vector3);
            getRot.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".rotation.eulerAngles";
            lib.Register(getRot);

            var getScale = Def.Make("unity.getscale", Cat, "Get Local Scale", ".localScale")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Масштаб", PortKind.Vector3);
            getScale.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".localScale";
            lib.Register(getScale);

            var getFwd = Def.Make("unity.getforward", Cat, "Get Forward", "Вектор вперёд")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Вперёд", PortKind.Vector3);
            getFwd.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".forward";
            lib.Register(getFwd);

            var getRight = Def.Make("unity.getright", Cat, "Get Right", "Вектор вправо")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Вправо", PortKind.Vector3);
            getRight.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".right";
            lib.Register(getRight);

            var getUp = Def.Make("unity.getup", Cat, "Get Up", "Вектор вверх")
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Вверх", PortKind.Vector3);
            getUp.Emit = (c, n) => GetTransformExpr(c, n, "tr") + ".up";
            lib.Register(getUp);

            var setPos = Def.Make("unity.setposition", Cat, "Set Position", "transform.position = ...")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("pos", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .Exec("out", "");
            setPos.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var pos = c.ResolveInput(n, c.Port(n, "pos"));
                c.Line($"if ({tr} != null) {tr}.position = {pos};");
                ChainOut(c, n, "out");
            };
            lib.Register(setPos);

            var setLocalPos = Def.Make("unity.setlocalposition", Cat, "Set Local Position", "transform.localPosition = ...")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("pos", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .Exec("out", "");
            setLocalPos.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var pos = c.ResolveInput(n, c.Port(n, "pos"));
                c.Line($"if ({tr} != null) {tr}.localPosition = {pos};");
                ChainOut(c, n, "out");
            };
            lib.Register(setLocalPos);

            var setRot = Def.Make("unity.setrotation", Cat, "Set Rotation", "transform.rotation (Euler)")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("rot", "Углы", PortKind.Vector3, literal: true, def_: "0,0,0")
                .Exec("out", "");
            setRot.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var rot = c.ResolveInput(n, c.Port(n, "rot"));
                c.Line($"if ({tr} != null) {tr}.rotation = Quaternion.Euler({rot});");
                ChainOut(c, n, "out");
            };
            lib.Register(setRot);

            var setScale = Def.Make("unity.setscale", Cat, "Set Local Scale", "transform.localScale = ...")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("scale", "Масштаб", PortKind.Vector3, literal: true, def_: "1,1,1")
                .Exec("out", "");
            setScale.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var s = c.ResolveInput(n, c.Port(n, "scale"));
                c.Line($"if ({tr} != null) {tr}.localScale = {s};");
                ChainOut(c, n, "out");
            };
            lib.Register(setScale);

            var translate = Def.Make("unity.translate", Cat, "Translate", "Сдвинуть по локальным осям")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("offset", "Сдвиг", PortKind.Vector3, literal: true, def_: "0,0,0")
                .EnumIn("space", "Система координат", new[] { "Self", "World" }, "Self")
                .Exec("out", "");
            translate.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var off = c.ResolveInput(n, c.Port(n, "offset"));
                var space = Def.Key(n, "space", c.NodeDef(n), "Self");
                c.Line($"if ({tr} != null) {tr}.Translate({off}, Space.{space});");
                ChainOut(c, n, "out");
            };
            lib.Register(translate);

            var rotate = Def.Make("unity.rotate", Cat, "Rotate", "Повернуть по локальным осям")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("angles", "Углы", PortKind.Vector3, literal: true, def_: "0,0,0")
                .Exec("out", "");
            rotate.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var a = c.ResolveInput(n, c.Port(n, "angles"));
                c.Line($"if ({tr} != null) {tr}.Rotate({a}, Space.Self);");
                ChainOut(c, n, "out");
            };
            lib.Register(rotate);

            var lookAt = Def.Make("unity.lookat", Cat, "Look At", "Повернуть объект к точке")
                .ExecIn()
                .AddIn("tr", "Объект", PortKind.GameObject, literal: false)
                .AddIn("target", "Цель (Vector3)", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("up", "Up", PortKind.Vector3, literal: true, def_: "0,1,0")
                .Exec("out", "");
            lookAt.EmitStatements = (c, n) =>
            {
                var tr = UnityNodes.GetTransformStmt(c, n, "tr");
                var t = c.ResolveInput(n, c.Port(n, "target"));
                var u = c.ResolveInput(n, c.Port(n, "up"));
                c.Line($"if ({tr} != null) {tr}.LookAt({t}, {u});");
                ChainOut(c, n, "out");
            };
            lib.Register(lookAt);

            // ---------- Активность, видимость ----------

            var setActive = Def.Make("unity.setactive", Cat, "Set Active", "Включить/выключить объект")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("value", "Активен", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            setActive.EmitStatements = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var v = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"if ({go} != null) {go}.SetActive({v});");
                ChainOut(c, n, "out");
            };
            lib.Register(setActive);

            var setRenderer = Def.Make("unity.setrendererenabled", Cat, "Set Renderer Enabled", "Включить/выключить рендер")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("value", "Видим", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            setRenderer.EmitStatements = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var v = c.ResolveInput(n, c.Port(n, "value"));
                var tmp = c.TempName("r");
                c.Line($"if ({go} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"var {tmp} = {go}.GetComponent<Renderer>();");
                c.Line($"if ({tmp} != null) {tmp}.enabled = {v};");
                c.PopIndent();
                c.Line("}");
                ChainOut(c, n, "out");
            };
            lib.Register(setRenderer);

            var setMaterialColor = Def.Make("unity.setmaterialcolor", Cat, "Set Material Color", "Цвет материала")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("color", "Цвет", PortKind.Color, literal: true, def_: "1,0,0,1")
                .AddIn("property", "Свойство шейдера", PortKind.String, literal: true, def_: "_Color")
                .Exec("out", "");
            setMaterialColor.EmitStatements = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var col = c.ResolveInput(n, c.Port(n, "color"));
                var prop = CodegenContext.Quote(Def.Key(n, "property", c.NodeDef(n), "_Color"));
                c.Line($"if ({go} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"var c_mat_{n.Id} = {go}.GetComponent<MeshRenderer>();");
                c.Line($"if (c_mat_{n.Id} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"c_mat_{n.Id}.material.SetColor({prop}, {col});");
                c.PopIndent();
                c.Line("}");
                c.PopIndent();
                c.Line("}");
                ChainOut(c, n, "out");
            };
            lib.Register(setMaterialColor);

            // ---------- Создание/удаление ----------

            var createPrimitive = Def.Make("unity.createprimitive", Cat, "Create Primitive", "GameObject.CreatePrimitive")
                .ExecIn()
                .EnumIn("type", "Тип", new[] { "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" }, "Cube")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "NewObject")
                .AddIn("position", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Объект", PortKind.GameObject)
                .Exec("execOut", "Дальше");
            createPrimitive.EmitStatements = (c, n) =>
            {
                var type = Def.Key(n, "type", c.NodeDef(n), "Cube");
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "NewObject"));
                var pos = c.ResolveInput(n, c.Port(n, "position"));
                var tmp = c.TempName("go");
                c.Line($"var {tmp} = GameObject.CreatePrimitive(PrimitiveType.{type});");
                c.Line($"{tmp}.name = {nm};");
                c.Line($"{tmp}.transform.position = {pos};");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.SetExternalValue(next, "in.go", tmp);
                }
                ChainOut(c, n, "execOut");
            };
            lib.Register(createPrimitive);

            var createEmpty = Def.Make("unity.createempty", Cat, "Create Empty", "new GameObject")
                .ExecIn()
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "NewObject")
                .AddIn("position", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Объект", PortKind.GameObject)
                .Exec("execOut", "Дальше");
            createEmpty.EmitStatements = (c, n) =>
            {
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "NewObject"));
                var pos = c.ResolveInput(n, c.Port(n, "position"));
                var tmp = c.TempName("go");
                c.Line($"var {tmp} = new GameObject({nm});");
                c.Line($"{tmp}.transform.position = {pos};");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.SetExternalValue(next, "in.go", tmp);
                }
                ChainOut(c, n, "execOut");
            };
            lib.Register(createEmpty);

            var destroy = Def.Make("unity.destroy", Cat, "Destroy", "Уничтожить объект")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .Exec("out", "");
            destroy.EmitStatements = (c, n) =>
            {
                var go = c.ResolveInput(n, c.Port(n, "go"));
                c.Line($"if ({go} != null) UnityEngine.Object.Destroy({go});");
                ChainOut(c, n, "out");
            };
            lib.Register(destroy);

            // ---------- Rigidbody / физика ----------

            var addForce = Def.Make("unity.addforce", Cat, "Add Force", "Rigidbody.AddForce")
                .ExecIn()
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddIn("force", "Сила", PortKind.Vector3, literal: true, def_: "0,0,10")
                .EnumIn("mode", "Режим", new[] { "Force", "Acceleration", "VelocityChange", "Impulse" }, "Force")
                .Exec("out", "");
            addForce.EmitStatements = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                var f = c.ResolveInput(n, c.Port(n, "force"));
                var mode = Def.Key(n, "mode", c.NodeDef(n), "Force");
                c.Line($"if ({rb} != null) {rb}.AddForce({f}, ForceMode.{mode});");
                ChainOut(c, n, "out");
            };
            lib.Register(addForce);

            var addTorque = Def.Make("unity.addtorque", Cat, "Add Torque", "Rigidbody.AddTorque")
                .ExecIn()
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddIn("torque", "Момент", PortKind.Vector3, literal: true, def_: "0,0,10")
                .EnumIn("mode", "Режим", new[] { "Force", "Acceleration", "VelocityChange", "Impulse" }, "Force")
                .Exec("out", "");
            addTorque.EmitStatements = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                var t = c.ResolveInput(n, c.Port(n, "torque"));
                var mode = Def.Key(n, "mode", c.NodeDef(n), "Force");
                c.Line($"if ({rb} != null) {rb}.AddTorque({t}, ForceMode.{mode});");
                ChainOut(c, n, "out");
            };
            lib.Register(addTorque);

            var setVelocity = Def.Make("unity.setvelocity", Cat, "Set Velocity", "Rigidbody.velocity = ...")
                .ExecIn()
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddIn("velocity", "Скорость", PortKind.Vector3, literal: true, def_: "0,0,0")
                .Exec("out", "");
            setVelocity.EmitStatements = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                var v = c.ResolveInput(n, c.Port(n, "velocity"));
                c.Line($"if ({rb} != null) {rb}.velocity = {v};");
                ChainOut(c, n, "out");
            };
            lib.Register(setVelocity);

            var getVelocity = Def.Make("unity.getvelocity", Cat, "Get Velocity", "Rigidbody.velocity")
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddOut("out", "Скорость", PortKind.Vector3);
            getVelocity.Emit = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                return $"({rb} != null ? {rb}.velocity : Vector3.zero)";
            };
            lib.Register(getVelocity);

            var getSpeed = Def.Make("unity.getspeed", Cat, "Get Speed", "Rigidbody.velocity.magnitude")
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddOut("out", "Скорость (м/с)", PortKind.Float);
            getSpeed.Emit = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                return $"({rb} != null ? {rb}.velocity.magnitude : 0f)";
            };
            lib.Register(getSpeed);

            var setGravity = Def.Make("unity.setgravity", Cat, "Set Use Gravity", "Rigidbody.useGravity")
                .ExecIn()
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddIn("value", "Гравитация", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            setGravity.EmitStatements = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                var v = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"if ({rb} != null) {rb}.useGravity = {v};");
                ChainOut(c, n, "out");
            };
            lib.Register(setGravity);

            var setKinematic = Def.Make("unity.setkinematic", Cat, "Set Is Kinematic", "Rigidbody.isKinematic")
                .ExecIn()
                .AddIn("rb", "Rigidbody", PortKind.Rigidbody, literal: false)
                .AddIn("value", "Кинематическое", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            setKinematic.EmitStatements = (c, n) =>
            {
                var rb = c.ResolveInput(n, c.Port(n, "rb"));
                var v = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"if ({rb} != null) {rb}.isKinematic = {v};");
                ChainOut(c, n, "out");
            };
            lib.Register(setKinematic);

            // ---------- Ввод ----------

            var keyDown = Def.Make("input.keydown", Cat, "Get Key Down", "Нажата ли клавиша в этом кадре")
                .EnumIn("key", "Клавиша", new[]
                {
                    "W","A","S","D","R","F","Space","LeftShift","LeftControl","E","Q","Z","X","C","V",
                    "Up","Down","Left","Right","LeftAlt","Tab","Escape","Return","B","H","N","M","P","K"
                }, "Space")
                .AddOut("out", "Нажата", PortKind.Bool);
            keyDown.Emit = (c, n) =>
            {
                var k = Def.Key(n, "key", c.NodeDef(n), "Space");
                return $"UnityEngine.Input.GetKeyDown(KeyCode.{k})";
            };
            lib.Register(keyDown);

            var keyHeld = Def.Make("input.key", Cat, "Get Key", "Удерживается ли клавиша")
                .EnumIn("key", "Клавиша", new[]
                {
                    "W","A","S","D","R","F","Space","LeftShift","LeftControl","E","Q","Z","X","C","V",
                    "Up","Down","Left","Right","LeftAlt","Tab","Escape","Return","B","H","N","M","P","K"
                }, "W")
                .AddOut("out", "Нажата", PortKind.Bool);
            keyHeld.Emit = (c, n) =>
            {
                var k = Def.Key(n, "key", c.NodeDef(n), "W");
                return $"UnityEngine.Input.GetKey(KeyCode.{k})";
            };
            lib.Register(keyHeld);

            var mouseDown = Def.Make("input.mousebuttondown", Cat, "Get Mouse Button Down", "UnityEngine.Input.GetMouseButtonDown")
                .AddIn("button", "Кнопка", PortKind.Int, literal: true, def_: 0)
                .AddOut("out", "Нажата", PortKind.Bool);
            mouseDown.Emit = (c, n) => $"UnityEngine.Input.GetMouseButtonDown({c.ResolveInput(n, c.Port(n, "button"))})";
            lib.Register(mouseDown);

            var mousePos = Def.Make("input.mouseposition", Cat, "Get Mouse Position", "Позиция курсора в пикселях")
                .AddOut("out", "Позиция", PortKind.Vector3);
            mousePos.Emit = (c, n) => "(Vector3)UnityEngine.Input.mousePosition";
            lib.Register(mousePos);

            // ---------- Время ----------

            var deltaTime = Def.Make("time.delta", Cat, "Get Delta Time", "Время с прошлого кадра")
                .AddOut("out", "Секунды", PortKind.Float);
            deltaTime.Emit = (c, n) => "Time.deltaTime";
            lib.Register(deltaTime);

            var time = Def.Make("time.time", Cat, "Get Time", "Время с запуска игры")
                .AddOut("out", "Секунды", PortKind.Float);
            time.Emit = (c, n) => "Time.time";
            lib.Register(time);

            var frameCount = Def.Make("time.framecount", Cat, "Get Frame Count", "Номер кадра")
                .AddOut("out", "Кадр", PortKind.Int);
            frameCount.Emit = (c, n) => "Time.frameCount";
            lib.Register(frameCount);

            var fixedDelta = Def.Make("time.fixeddelta", Cat, "Get Fixed Delta Time", "Шаг физики")
                .AddOut("out", "Секунды", PortKind.Float);
            fixedDelta.Emit = (c, n) => "Time.fixedDeltaTime";
            lib.Register(fixedDelta);

            // ---------- Прочее ----------

            var findAny = Def.Make("unity.findallbyname", Cat, "Find All By Name",
                    "Resources.FindObjectsOfTypeAll — ищет даже неактивные объекты")
                .AddIn("name", "Имя (часть имени)", PortKind.String, literal: true, def_: "SATSUMA")
                .AddIn("requireRigidbody", "Только с Rigidbody", PortKind.Bool, literal: true, def_: true)
                .AddOut("out", "Объект", PortKind.GameObject);
            findAny.Emit = (c, n) =>
            {
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), ""));
                var needRb = Def.KeyBool(n, "requireRigidbody", c.NodeDef(n), true);
                var tmp = c.TempName("found");
                if (needRb)
                {
                    return $"(c_FindByNameWithRigidbody({nm}) ?? null)";
                }
                return $"(c_FindByName({nm}) ?? null)";
            };
            lib.Register(findAny);

            var mainCamera = Def.Make("unity.maincamera", Cat, "Get Main Camera", "Camera.main")
                .AddOut("out", "Камера", PortKind.GameObject);
            mainCamera.Emit = (c, n) => "((Camera.main != null) ? Camera.main.gameObject : null)";
            lib.Register(mainCamera);

            var cursorVisible = Def.Make("unity.cursvisible", Cat, "Set Cursor Visible",
                    "Показать/спрятать курсор (нужно при открытии меню)")
                .ExecIn()
                .AddIn("visible", "Показывать", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            cursorVisible.EmitStatements = (c, n) =>
            {
                var v = c.ResolveInput(n, c.Port(n, "visible"));
                c.Line($"Cursor.visible = {v};");
                if (Def.KeyBool(n, "visible", c.NodeDef(n), true))
                    c.Line("Cursor.lockState = CursorLockMode.None;");
                ChainOut(c, n, "out");
            };
            lib.Register(cursorVisible);

            var cursorLock = Def.Make("unity.cursorlock", Cat, "Set Cursor Locked", "Заблокировать курсор в центре")
                .ExecIn()
                .AddIn("locked", "Заблокирован", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            cursorLock.EmitStatements = (c, n) =>
            {
                var v = c.ResolveInput(n, c.Port(n, "locked"));
                c.Line($"Cursor.lockState = {v} ? CursorLockMode.Locked : CursorLockMode.None;");
                c.Line($"Cursor.visible = {v} ? false : true;");
                ChainOut(c, n, "out");
            };
            lib.Register(cursorLock);

            var timeScale = Def.Make("unity.timescale", Cat, "Get Time Scale", "Текущая скорость игры")
                .AddOut("out", "Скорость", PortKind.Float);
            timeScale.Emit = (c, n) => "Time.timeScale";
            lib.Register(timeScale);

            var setTimeScale = Def.Make("unity.settimescale", Cat, "Set Time Scale",
                    "Изменить скорость игры: 0 = пауза, 1 = обычная, 60 = ускорение")
                .ExecIn()
                .AddIn("value", "Скорость", PortKind.Float, literal: true, def_: 1f)
                .Exec("out", "");
            setTimeScale.EmitStatements = (c, n) =>
            {
                var v = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"Time.timeScale = {v};");
                ChainOut(c, n, "out");
            };
            lib.Register(setTimeScale);

            var pauseGame = Def.Make("unity.pausegame", Cat, "Pause / Resume Game", "Пауза и снятие паузы")
                .ExecIn()
                .AddIn("paused", "На паузе", PortKind.Bool, literal: true, def_: true)
                .Exec("out", "");
            pauseGame.EmitStatements = (c, n) =>
            {
                var v = c.ResolveInput(n, c.Port(n, "paused"));
                c.Line($"Time.timeScale = {v} ? 0f : 1f;");
                ChainOut(c, n, "out");
            };
            lib.Register(pauseGame);

            var teleportSafe = Def.Make("unity.teleportsafe", Cat, "Teleport To Object (safe)",
                    "Переместить игрока рядом с объектом, на землю снаружи. " +
                    "Временно отключает CharacterController, чтобы не застрять в кузове.")
                .ExecIn()
                .AddIn("player", "Игрок", PortKind.GameObject, literal: false)
                .AddIn("target", "Цель", PortKind.GameObject, literal: false)
                .AddOut("out", "", PortKind.Execution);
            teleportSafe.EmitStatements = (c, n) =>
            {
                var player = c.ResolveInput(n, c.Port(n, "player"));
                var target = c.ResolveInput(n, c.Port(n, "target"));
                c.Line($"c_TeleportSafe({player}, {target});");
                ChainOut(c, n, "out");
            };
            lib.Register(teleportSafe);

            var getAxis = Def.Make("unity.getaxis", Cat, "Get Input Axis", "Input.GetAxis (например \"Mouse ScrollWheel\")")
                .EnumIn("axis", "Ось", new[] { "Mouse ScrollWheel", "Mouse X", "Mouse Y",
                                                "Horizontal", "Vertical" }, "Mouse ScrollWheel")
                .AddOut("out", "Значение", PortKind.Float);
            getAxis.Emit = (c, n) =>
            {
                var axis = Def.Key(n, "axis", c.NodeDef(n), "Mouse ScrollWheel");
                return $"Input.GetAxis({CodegenContext.Quote(axis)})";
            };
            lib.Register(getAxis);

            var player = Def.Make("unity.player", Cat, "Get Player", "Объект игрока (PLAYER)")
                .AddOut("out", "Объект", PortKind.GameObject);
            player.Emit = (c, n) => "GameObject.Find(\"PLAYER\")";
            lib.Register(player);

            var car = Def.Make("unity.car", Cat, "Get Current Car", "Текущая машина игрока")
                .AddOut("out", "Объект", PortKind.GameObject);
            car.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                return "c_CurrentCarObject()";
            };
            lib.Register(car);

            var gameState = Def.Make("unity.isinmenu", Cat, "Is In Menu", "Мы в главном меню?")
                .AddOut("out", "В меню", PortKind.Bool);
            gameState.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                return "(ModLoader.GetMod(ID) == null || ModLoader.GetMod(ID).LoadInMenu);";
            };
            lib.Register(gameState);

            var sceneName = Def.Make("unity.scenename", Cat, "Get Scene Name", "Имя текущей сцены")
                .AddOut("out", "Сцена", PortKind.String);
            sceneName.Emit = (c, n) => "UnityEngine.SceneManagement.SceneManager.GetActiveScene().name";
            lib.Register(sceneName);

            // ---------- Логирование ----------

            var log = Def.Make("unity.log", Cat, "Debug Log", "Вывод в консоль MSCLoader")
                .ExecIn()
                .AddIn("message", "Сообщение", PortKind.String, literal: true, def_: "Hello")
                .AddIn("level", "Уровень", PortKind.String, literal: true, def_: "Log")
                .Exec("out", "");
            log.EmitStatements = (c, n) =>
            {
                var msg = c.ResolveInput(n, c.Port(n, "message"));
                var lvl = Def.Key(n, "level", c.NodeDef(n), "Log");
                c.Use("MSCLoader");
                var line = lvl switch
                {
                    "Warning" => $"ModConsole.Warning({msg});",
                    "Error" => $"ModConsole.Error({msg});",
                    _ => $"ModConsole.Log({msg});"
                };
                c.Line(line);
                ChainOut(c, n, "out");
            };
            lib.Register(log);

            var showMessage = Def.Make("unity.showmessage", Cat, "Show Message", "Всплывающее сообщение в игре")
                .ExecIn()
                .AddIn("message", "Текст", PortKind.String, literal: true, def_: "Событие")
                .AddIn("title", "Заголовок", PortKind.String, literal: true, def_: "Мод")
                .AddIn("subtitle", "Подзаголовок", PortKind.String, literal: true, def_: "")
                .Exec("out", "");
            showMessage.EmitStatements = (c, n) =>
            {
                var msg = c.ResolveInput(n, c.Port(n, "message"));
                var title = CodegenContext.Quote(Def.Key(n, "title", c.NodeDef(n), "Мод"));
                var sub = CodegenContext.Quote(Def.Key(n, "subtitle", c.NodeDef(n), ""));
                c.Use("MSCLoader");
                c.Line($"ModUI.ShowMessage({msg}, {title});");
                ChainOut(c, n, "out");
            };
            lib.Register(showMessage);
        }

        // ---------- Вспомогательное ----------

        internal static string GetTransformExpr(CodegenContext c, NodeInstance n, string portId)
        {
            var raw = c.ResolveInput(n, c.Port(n, portId));
            return $"({raw} != null ? {raw}.transform : null)";
        }

        internal static string GetTransformStmt(CodegenContext c, NodeInstance n, string portId)
        {
            var raw = c.ResolveInput(n, c.Port(n, portId));
            return $"({raw} != null ? {raw}.transform : null)";
        }

        internal static void ChainOut(CodegenContext c, NodeInstance n, string port)
        {
            foreach (var conn in c.OutgoingAll(n, port))
            {
                var next = c.Graph.Find(conn.ToNode);
                if (next != null) c.RunStatements(next, c.NodeDef(next));
            }
        }
    }
}