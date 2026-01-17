using System.Diagnostics;
using System;
using System.Diagnostics;
using System.Threading;

namespace ConsoleCube3D
{
    class Program
    {
        static int width = 100;
        static int height = 30;


        static char emptyChar = ' ';
        static char cubeChar = '#';
        static char debrisChar = '*';
        static char floorChar = '.';

        // Куб
        static readonly (double x, double y, double z)[] cubeVerts =
        {
        (-1,-1,-1), (1,-1,-1), (1,1,-1), (-1,1,-1),
        (-1,-1, 1), (1,-1, 1), (1,1, 1), (-1,1, 1)
    };
        static readonly (int a, int b)[] cubeEdges =
        {
        (0,1),(1,2),(2,3),(3,0),
        (4,5),(5,6),(6,7),(7,4),
        (0,4),(1,5),(2,6),(3,7)
    };

        // Проекция (простой вариант)
        // Экранный X = cx + x / z * projK, Y = cy - y / z * projK
        static double projK = 35.0; // масштаб перспективы (увеличьте/уменьшите при необходимости)

        // Камера и позиция куба
        static double cameraZ = 0.0; // камера в (0,0,0), смотрит вдоль +Z
        static double baseZ = 8.0;   // куб на этой глубине хорошо виден
        static double floorY = -3.0;

        // Вращение
        static double angleX = 0;
        static double angleY = 0;
        static double angleZ = 0;

        // Физика
        enum Mode { Rotate, Physics }
        static Mode mode = Mode.Rotate;

        static (double x, double y, double z) cubePos = (0, 0, 0);
        static (double x, double y, double z) cubeVel = (0, 0, 0);
        static (double x, double y, double z) cubeAngVel = (0, 0, 0);
        static double gravity = -9.8;
        static double restitution = 0.35;
        static double breakSpeedThreshold = 8.0;
        static bool broken = false;

        class Debris
        {
            public (double x, double y, double z) p0;
            public (double x, double y, double z) p1;
            public (double x, double y, double z) v;
            public double life;
        }
        static Debris[] debris = Array.Empty<Debris>();

        static void Main()
        {
            Console.CursorVisible = false;
            TryApplyConsoleSize();

            var buffer = new char[width * height];
            var sw = new Stopwatch();
            sw.Start();

            double last = sw.Elapsed.TotalSeconds;
            double targetFps = 30.0;
            double frameTime = 1.0 / targetFps;

            ResetPhysics();

            while (true)
            {
                // Подстройка к размеру окна (если пользователь изменил окно)
                if (TryApplyConsoleSizeChanged(ref buffer))
                {
                    // пересчитать параметры при ресайзе можно при желании
                }

                // Тайминг
                double now = sw.Elapsed.TotalSeconds;
                double dt = now - last;
                if (dt < frameTime)
                {
                    int sleep = (int)((frameTime - dt) * 1000);
                    if (sleep > 0) Thread.Sleep(sleep);
                    now = sw.Elapsed.TotalSeconds;
                    dt = now - last;
                }
                last = now;
                if (dt > 0.05) dt = 0.05; // защита от рывков

                // Ввод
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Escape) return;
                    if (key == ConsoleKey.R)
                    {
                        if (mode == Mode.Rotate)
                        {
                            mode = Mode.Physics;
                            ResetPhysics();
                        }
                        else
                        {
                            mode = Mode.Rotate;
                            broken = false;
                        }
                    }
                }

                // Обновление
                if (mode == Mode.Rotate)
                {
                    angleX += dt * 0.9;
                    angleY += dt * 1.2;
                    angleZ += dt * 0.6;
                }
                else
                {
                    UpdatePhysics(dt);
                }

                // Рендер
                ClearBuffer(buffer, emptyChar);
                DrawHeader();

                if (mode == Mode.Rotate)
                {
                    RenderCube(buffer, angleX, angleY, angleZ, (0, 0.5, baseZ));
                }
                else
                {
                    if (!broken)
                        RenderCube(buffer, angleX, angleY, angleZ, (cubePos.x, cubePos.y, cubePos.z + baseZ));
                    else
                        RenderDebris(buffer);
                }

                DrawFloor(buffer);

                // Вывод
                DrawBuffer(buffer);
            }
        }

        static void ResetPhysics()
        {
            cubePos = (0, 1.2, 0);
            cubeVel = (0, 0, 0);
            cubeAngVel = (1.5, 0.9, 1.1);
            angleX = 0; angleY = 0; angleZ = 0;
            broken = false;
            debris = Array.Empty<Debris>();
        }

        static void UpdatePhysics(double dt)
        {
            if (!broken)
            {
                angleX += cubeAngVel.x * dt;
                angleY += cubeAngVel.y * dt;
                angleZ += cubeAngVel.z * dt;

                cubeVel.y += gravity * dt;

                cubePos.x += cubeVel.x * dt;
                cubePos.y += cubeVel.y * dt;
                cubePos.z += cubeVel.z * dt;

                double halfSize = 1.0;
                if (cubePos.y - halfSize <= floorY)
                {
                    double impact = Math.Abs(cubeVel.y);
                    cubePos.y = floorY + halfSize;
                    cubeVel.y = -cubeVel.y * restitution;
                    cubeVel.x *= 0.9;
                    cubeVel.z *= 0.9;

                    if (impact > breakSpeedThreshold)
                    {
                        BreakCube();
                    }
                }
            }
            else
            {
                foreach (var d in debris)
                {
                    d.v = (d.v.x, d.v.y + gravity * dt, d.v.z);
                    d.p0 = (d.p0.x + d.v.x * dt, d.p0.y + d.v.y * dt, d.p0.z + d.v.z * dt);
                    d.p1 = (d.p1.x + d.v.x * dt, d.p1.y + d.v.y * dt, d.p1.z + d.v.z * dt);

                    if (d.p0.y < floorY) d.p0 = (d.p0.x, floorY, d.p0.z);
                    if (d.p1.y < floorY) d.p1 = (d.p1.x, floorY, d.p1.z);

                    d.life -= dt;
                }
                debris = Array.FindAll(debris, d => d.life > 0);
            }
        }

        static void BreakCube()
        {
            broken = true;

            var rotVerts = new (double x, double y, double z)[cubeVerts.Length];
            for (int i = 0; i < cubeVerts.Length; i++)
            {
                var v = cubeVerts[i];
                var rv = Rotate(v, angleX, angleY, angleZ);
                rotVerts[i] = (rv.x + cubePos.x, rv.y + cubePos.y, rv.z + cubePos.z + baseZ);
            }

            var rnd = new Random();
            debris = new Debris[cubeEdges.Length];
            for (int i = 0; i < cubeEdges.Length; i++)
            {
                var e = cubeEdges[i];
                var p0 = rotVerts[e.a];
                var p1 = rotVerts[e.b];

                var vx = cubeVel.x + (rnd.NextDouble() - 0.5) * 10.0;
                var vy = Math.Max(3.0, Math.Abs(cubeVel.y)) + rnd.NextDouble() * 5.0;
                var vz = cubeVel.z + (rnd.NextDouble() - 0.5) * 10.0;

                debris[i] = new Debris
                {
                    p0 = p0,
                    p1 = p1,
                    v = (vx, vy, vz),
                    life = 3.0 + rnd.NextDouble() * 2.0
                };
            }
        }

        static (double x, double y, double z) Rotate((double x, double y, double z) v, double ax, double ay, double az)
        {
            double cx = Math.Cos(ax), sx = Math.Sin(ax);
            double cy = Math.Cos(ay), sy = Math.Sin(ay);
            double cz = Math.Cos(az), sz = Math.Sin(az);

            double y1 = v.y * cx - v.z * sx;
            double z1 = v.y * sx + v.z * cx;
            double x1 = v.x;

            double x2 = x1 * cy + z1 * sy;
            double z2 = -x1 * sy + z1 * cy;
            double y2 = y1;

            double x3 = x2 * cz - y2 * sz;
            double y3 = x2 * sz + y2 * cz;
            double z3 = z2;

            return (x3, y3, z3);
        }

        static void RenderCube(char[] buffer, double ax, double ay, double az, (double x, double y, double z) pos)
        {
            var vertsWorld = new (double x, double y, double z)[cubeVerts.Length];
            for (int i = 0; i < cubeVerts.Length; i++)
            {
                var v = Rotate(cubeVerts[i], ax, ay, az);
                vertsWorld[i] = (v.x + pos.x, v.y + pos.y, v.z + pos.z);
            }

            foreach (var e in cubeEdges)
            {
                ProjectAndDrawLine(buffer, vertsWorld[e.a], vertsWorld[e.b], cubeChar);
            }
        }

        static void RenderDebris(char[] buffer)
        {
            foreach (var d in debris)
            {
                ProjectAndDrawLine(buffer, d.p0, d.p1, debrisChar);
            }
        }

        static void ProjectAndDrawLine(char[] buffer, (double x, double y, double z) a, (double x, double y, double z) b, char ch)
        {
            if (!Project(a, out int x0, out int y0)) return;
            if (!Project(b, out int x1, out int y1)) return;
            DrawLine(buffer, x0, y0, x1, y1, ch);
        }

        static bool Project((double x, double y, double z) p, out int sx, out int sy)
        {
            // Отбрасываем объекты позади камеры (z <= 0 относительно камеры)
            double zCam = p.z - cameraZ;
            if (zCam <= 0.1)
            {
                sx = sy = 0; return false;
            }

            double cx = width / 2.0;
            double cy = height / 2.0;

            double nx = p.x / zCam * projK;
            double ny = p.y / zCam * projK;

            sx = (int)(cx + nx);
            sy = (int)(cy - ny);

            if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                return false;
            return true;
        }

        static void DrawLine(char[] buffer, int x0, int y0, int x1, int y1, char ch)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int x = x0, y = y0;
            while (true)
            {
                PutPixel(buffer, x, y, ch);
                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        static void PutPixel(char[] buffer, int x, int y, char ch)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            buffer[y * width + x] = ch;
        }

        static void ClearBuffer(char[] buffer, char ch)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = ch;
        }

        static void DrawBuffer(char[] buffer)
        {
            Console.SetCursorPosition(0, 0);
            // Первая строка — заголовок уже выведем отдельно, затем всё остальное
            for (int y = 0; y < height; y++)
            {
                int start = y * width;
                Console.Write(buffer, start, width);
                if (y != height - 1) Console.Write('\n');
            }
        }

        static void DrawHeader()
        {
            string info = $"Mode: {mode}  (R - toggle, Esc - exit)";
            if (info.Length > width) info = info.Substring(0, width);
            Console.SetCursorPosition(0, 0);
            Console.Write(info.PadRight(width));
        }

        static void DrawFloor(char[] buffer)
        {
            // Рисуем линию пола: возьмем несколько точек вдоль X и на разных z,
            // чтобы получилось плотнее.
            int stripes = 2;
            for (int s = 0; s < stripes; s++)
            {
                double z = baseZ + 6 + s * 4; // линии дальше
                for (int x = 0; x < width; x++)
                {
                    double nx = (x - width / 2.0) / (projK) * z;
                    var p = (nx, floorY, z);
                    if (Project(p, out int sx, out int sy))
                        PutPixel(buffer, sx, sy, floorChar);
                }
            }
        }

        static bool TryApplyConsoleSizeChanged(ref char[] buffer)
        {
            int w = width, h = height;
            bool changed = TryApplyConsoleSize();
            if (changed)
            {
                if (width < 40) width = 40;
                if (height < 20) height = 20;
                buffer = new char[width * height];
            }
            return changed;
        }

        static bool TryApplyConsoleSize()
        {
            try
            {
                int newW = Console.WindowWidth;
                int newH = Console.WindowHeight;
                if (newW != width || newH != height)
                {
                    width = newW;
                    height = newH;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}