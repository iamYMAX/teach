using System.Collections.Generic;
using System.Linq;
using OmnieyeBot.Models;

namespace OmnieyeBot.Services
{
    public class CourseService
    {
        private static readonly List<CourseModule> _modules = new()
        {
            new CourseModule
            {
                Id = "module1",
                Title = "Введение в системное администрирование",
                Lessons = new List<Lesson>
                {
                    new Lesson
                    {
                        Id = "lesson1_1",
                        Title = "Кто такой системный администратор?",
                        Content = "Системный администратор — это специалист, отвечающий за стабильную работу компьютерной техники, сети и программного обеспечения. Он обеспечивает информационную безопасность компании, занимается резервным копированием данных и восстановлением системы после сбоев."
                    },
                    new Lesson
                    {
                        Id = "lesson1_2",
                        Title = "Обзор ОС: Windows Server и Linux",
                        Content = "Администраторы обычно работают с двумя семействами ОС: Windows Server и Linux. Windows Server предлагает графический интерфейс и тесную интеграцию с продуктами Microsoft. Linux — это гибкая и мощная система с открытым исходным кодом, популярная для веб-серверов и облачных вычислений."
                    }
                }
            },
            new CourseModule
            {
                Id = "module2",
                Title = "Основы сетей",
                Lessons = new List<Lesson>
                {
                    new Lesson
                    {
                        Id = "lesson2_1",
                        Title = "Модель OSI и TCP/IP",
                        Content = "Модель OSI (Open Systems Interconnection) — это концептуальная модель, которая характеризует и стандартизирует коммуникационные функции телекоммуникационной или вычислительной системы безотносительно к её базовой внутренней структуре и технологии. Модель TCP/IP — это практическая реализация, используемая в Интернете."
                    },
                    new Lesson
                    {
                        Id = "lesson2_2",
                        Title = "IP-адресация и подсети",
                        Content = "IP-адрес — это уникальный числовой идентификатор устройства в компьютерной сети, работающей по протоколу IP. Подсети позволяют логически разделять большую сеть на более мелкие и управляемые части."
                    }
                }
            }
        };

        public List<CourseModule> GetCourseModules()
        {
            return _modules;
        }

        public CourseModule GetModuleById(string moduleId)
        {
            return _modules.FirstOrDefault(m => m.Id == moduleId);
        }

        public Lesson GetLessonById(string moduleId, string lessonId)
        {
            var module = GetModuleById(moduleId);
            return module?.Lessons.FirstOrDefault(l => l.Id == lessonId);
        }
    }
}
