using Npgsql;
using NpgsqlTypes;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using static XMLSaver.Program;

namespace XMLSaver
{
    internal class Program
    {
        static void Main(string[] args)
        {
            CultureInfo culture = new CultureInfo("en-US");
            culture.NumberFormat.NumberDecimalSeparator = ".";
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;

            Console.Write("Введите данные для подключения к БД: ");
            // Подключение к БД
            string connectionString = Console.ReadLine(); //"Host=localhost; Port=5432; Database=Shop; Username=postgres; Password=123;";

            Console.Write("Введите путь к файлу: ");
            // Путь к файлу
            string xmlFilePath = Console.ReadLine(); //@"E:\\testXML.xml";

            try
            {
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();
                    var orders = new List<Order>();

                    try
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(xmlFilePath);

                        XmlNodeList ordersXML = xmlDoc.SelectNodes("/orders/order");

                        foreach (XmlNode order in ordersXML)
                        {
                            var newOrder = new Order();
                            newOrder.Date = DateOnly.Parse(order.SelectSingleNode("reg_date").InnerText);
                            newOrder.No = int.Parse(order.SelectSingleNode("no").InnerText);
                            newOrder.User = new User()
                            {
                                FIO = order.SelectSingleNode("user/fio").InnerText,
                                Email = order.SelectSingleNode("user/email").InnerText
                            };

                            newOrder.Sells = new List<Sell>();
                            XmlNodeList products = order.SelectNodes("product");
                            foreach (XmlNode product in products)
                            {
                                var newSell = new Sell();
                                newSell.Quantity = int.Parse(product.SelectSingleNode("quantity").InnerText);
                                newSell.Price = Convert.ToDouble(product.SelectSingleNode("price").InnerText, CultureInfo.InvariantCulture);
                                newSell.Product = new Product()
                                {
                                    Name = product.SelectSingleNode("name").InnerText
                                };

                                newOrder.Sells.Add(newSell);

                            }
                            orders.Add(newOrder);
                        }

                        try
                        {
                            // Получаем список необходимых продуктов из БД
                            var allProducts = GetProducts(orders.SelectMany(x => x.Sells.Select(y => $"'{y.Product.Name}'")).ToArray(), connection);
                            var newProductNames = orders
                                .SelectMany(x => x.Sells.Select(y => y.Product.Name)
                                    .Where(y => !allProducts.Select(z => z.Name).Contains(y)))
                                .Distinct();
                            // Добавляем новые продукты
                            foreach (var productName in newProductNames)
                            {
                                var cmd = new NpgsqlCommand($"INSERT INTO \"Product\" (\"Name\") VALUES ('{productName}')", connection);
                                cmd.ExecuteNonQuery();
                            }
                            // Обновляем список необходимых продуктов
                            allProducts = GetProducts(orders.SelectMany(x => x.Sells.Select(y => $"'{y.Product.Name}'")).ToArray(), connection);

                            // Получаем список необходимых пользователей из БД
                            var allUsers = GetUsers(orders.Select(x => $"'{x.User.FIO}'").ToArray(), connection);
                            var newUsers = orders
                                .Where(x => !allUsers.Select(y => y.FIO).Contains(x.User.FIO))
                                .Select(x => new User
                                {
                                    FIO = x.User.FIO,
                                    Email = x.User.Email
                                })
                                .Distinct();
                            // Добавляем новых пользователей
                            foreach (var user in newUsers)
                            {
                                var cmd = new NpgsqlCommand($"INSERT INTO \"User\" (\"FIO\", \"Email\") VALUES ('{user.FIO}', '{user.Email}')", connection);
                                cmd.ExecuteNonQuery();
                            }
                            // Обновляем список необходимых пользователей
                            allUsers = GetUsers(orders.Select(x => $"'{x.User.FIO}'").ToArray(), connection);

                            // Добавляем новых пользователей
                            foreach (var order in orders)
                            {
                                var clientId = allUsers.Where(x => x.FIO == order.User.FIO).First().Id;
                                // Сохраняем новый заказ
                                var cmd = new NpgsqlCommand($"INSERT INTO \"Order\" (\"ClientId\", \"Date\", \"No\") VALUES ('{clientId}', '{order.Date.ToString("yyyy-MM-dd")}', '{order.No}') RETURNING \"Id\"", connection);
                                var orderId = (int)cmd.ExecuteScalar();

                                // Сохраняем все позиции из этого заказа
                                foreach (var sell in order.Sells)
                                {
                                    var productId = allProducts.Where(x => x.Name == sell.Product.Name).First().Id;
                                    var cmdSell = new NpgsqlCommand($"INSERT INTO \"Sell\" (\"OrderId\", \"ProductId\", \"Quantity\", \"Price\") VALUES ('{orderId}', '{productId}', '{sell.Quantity}', '{sell.Price}')", connection);
                                    cmdSell.ExecuteNonQuery();
                                }
                            }

                            Console.WriteLine("Данные успешно загружены!");
                        }
                        catch
                        {
                            Console.WriteLine("Ошибка записи данных в БД!");
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Ошибка чтения файла!");
                    }
                }
            }
            catch
            {
                Console.WriteLine("Ошибка подключения к БД!");
            }
                
            Console.ReadLine();
        }

        // Метод получения всех записей из таблицы Product с названием продукта из списка
        public static List<Product> GetProducts(string[] filter, NpgsqlConnection connection)
        {
            var products = new List<Product>();
            var param = string.Join(",", filter);
            var cmd = new NpgsqlCommand($"SELECT * FROM \"Product\" WHERE \"Name\" IN ({param})", connection);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    Product product = new Product();
                    product.Id = Convert.ToInt32(reader["Id"]);
                    product.Name = reader["Name"].ToString();
                    products.Add(product);
                }
            }
            return products;
        }

        // Метод получения всех записей из таблицы User с ФИО из списка
        public static List<User> GetUsers(string[] filter, NpgsqlConnection connection)
        {
            var users = new List<User>();
            var param = string.Join(",", filter);
            var cmd = new NpgsqlCommand($"SELECT * FROM \"User\" WHERE \"FIO\" IN ({param})", connection);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    User user = new User();
                    user.Id = Convert.ToInt32(reader["Id"]);
                    user.FIO = reader["FIO"].ToString();
                    user.Email = reader["Email"].ToString();
                    users.Add(user);
                }
            }
            return users;
        }

        // Класс пользователя
        public class User
        {
            public int Id { get; set; }
            public string? FIO { get; set; }
            public string? Email { get; set; }
        }

        // Класс заказа
        public class Order
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public User? User { get; set; }
            public DateOnly Date { get; set; }
            public int No { get; set; }
            public List<Sell>? Sells {  get; set; }
        }

        // Класс покупки
        public class Sell
        {
            public int Id { get; set; }
            public int OrderId { get; set; }
            public int ProductId { get; set; }
            public Product? Product { get; set; }
            public int Quantity { get; set; }
            public double Price { get; set; }
        }

        // Класс продукта
        public class Product
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }
    }
}
