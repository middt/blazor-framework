using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Threading;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Security
{
    public class CircuitHandlerService : CircuitHandler
    {
        //public string CircuitId { get; set; }
        //public SessionData sessionData { get; set; }

        //public CircuitHandlerService(SessionData sessionData)
        //{
        //    this.sessionData = sessionData;
        //}

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            //CircuitId = circuit.Id;
            return base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            //when the circuit is closing, attempt to delete the session
            //  this will happen if the current circuit represents the main window
            //sessionData.Delete(circuit.Id);

            return base.OnCircuitClosedAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            return base.OnConnectionDownAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            return base.OnConnectionUpAsync(circuit, cancellationToken);
        }
    }
}
