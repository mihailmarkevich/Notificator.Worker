using Notificator.SharedServices;
using Notificator.SharedServices.Models;
using System.Net;
using System.Net.Mail;

namespace Notificator.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private IEventRepository _eventRepository;
        private string _recipientAddress;
        private SmtpSettings _smtpSettings;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Connection string isn't defined!");
                Environment.Exit(1);
            }
            _eventRepository = new EventRepository(connectionString);

            _recipientAddress = (string)_configuration.GetValue<string>("RecipientAddress");
            if (string.IsNullOrEmpty(_recipientAddress))
            {
                _logger.LogError("Recipient email address isn't defined!");
                Environment.Exit(1);
            }

            _smtpSettings = _configuration.GetSection("SmtpSettings").Get<SmtpSettings>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Worker running at: {time}", DateTimeOffset.Now);

                    List<EventModel> events = _eventRepository.GetAllCurrent().ToList();

                    foreach (EventModel eventModel in events)
                    {
                        this.Notify(eventModel);

                        _eventRepository.UpdateStatus(eventModel.Id);

                        _logger.LogInformation("Event {name} notification is sent.", eventModel.Name);
                    }

                    //Wait a minute
                    await Task.Delay(60000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Send notifications via email through the SMTP protocol
        /// </summary>
        /// <param name="eventModel"></param>
        private void Notify(EventModel eventModel)
        {
            var fromAddress = new MailAddress(_smtpSettings.SenderEmail, _smtpSettings.SenderName);
            var toAddress = new MailAddress(_recipientAddress);

            using SmtpClient smtp = new SmtpClient
            {
                Host = _smtpSettings.SmtpServer,
                Port = _smtpSettings.Port,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, _smtpSettings.Password)
            };

            string message = $"Event '{eventModel.Name}' just happened!";
            if (!string.IsNullOrEmpty(eventModel.Description)) 
            {
                message += $"\r\nDetails: {eventModel.Description}";
            }

            using var emailMessage = new MailMessage(fromAddress, toAddress)
            {
                Subject = "Event Notification",
                Body = message
            };
            
            smtp.Send(emailMessage);
        }

    }
}
