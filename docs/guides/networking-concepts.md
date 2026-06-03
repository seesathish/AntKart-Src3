# Networking & Kubernetes Concepts — Primer

A learner-friendly reference for the networking and Kubernetes ideas behind the platform's cloud infrastructure. It explains the concepts first, then shows how they shape the way the network is sized. No prior cloud-networking experience is assumed.

---

## 1. IP Addressing & CIDR

**What an IP address is.** An IPv4 address is a 32-bit number that identifies a device on a network. It is written as four 8-bit numbers (octets), each 0–255, separated by dots — for example `10.0.4.17`. Every machine that talks on a network needs one.

**What CIDR notation means.** A network is a *range* of addresses, written as an address followed by a slash and a number: `10.0.0.0/16`. The number after the slash — the **prefix length** — says how many of the 32 bits are fixed as the *network* portion. The remaining bits are free to number individual *hosts*.

- A `/16` fixes the first 16 bits, leaving 16 bits for hosts → a large range.
- A `/24` fixes the first 24 bits, leaving 8 bits for hosts → a small range.

So the **total addresses** in a block is `2^(32 − n)`, where `n` is the prefix length.

**Usable addresses in Azure.** Azure reserves **5 addresses in every subnet** (the network address, the default gateway, two for Azure-internal DNS mapping, and the broadcast address). So **usable = total − 5**.

| CIDR | Host bits (32 − n) | Total addresses (2^(32−n)) | Usable in Azure (− 5) |
|------|--------------------|----------------------------|-----------------------|
| `/16` | 16 | 65,536 | 65,531 |
| `/22` | 10 | 1,024 | 1,019 |
| `/24` | 8 | 256 | 251 |
| `/26` | 6 | 64 | 59 |
| `/27` | 5 | 32 | 27 |

> **Rule of thumb:** a **smaller slash number means a bigger block**. `/16` is far larger than `/24`. Each step *down* in the prefix (e.g. `/24` → `/22`) roughly *quadruples* the size (two more host bits = 4× the addresses).

---

## 2. VNet, Subnet, and NSG

**Virtual Network (VNet)** — your own private, isolated network in the cloud, defined by a large address range (commonly a `/16`). Nothing outside the VNet can reach into it unless you explicitly allow it. It is the private address space everything else is carved from.

**Subnet** — a slice of the VNet's range, dedicated to a particular workload or tier (for example: one subnet for the Kubernetes cluster, one for private endpoints, one for the gateway). Subnets within a VNet **must not overlap** — each owns a distinct portion of the VNet's addresses. Resources draw their IP from the subnet they live in.

**Network Security Group (NSG)** — a **stateful firewall** attached to a subnet (or a network interface). It holds an ordered list of **allow/deny rules** that match on source, destination, port, and protocol, evaluated by priority.

> **"Stateful"** means the NSG tracks connections: if you allow an inbound request, the **return traffic is automatically allowed** — you don't have to write a second rule for the response. (A *stateless* firewall would need an explicit rule for each direction.)

---

## 3. Kubernetes Fundamentals (smallest to largest)

It helps to build the picture from the smallest piece up:

- **Container** — a packaged application plus everything it needs to run (runtime, libraries, config), isolated from other processes. It is the unit you build and ship.
- **Pod** — the **smallest deployable unit** in Kubernetes. A pod wraps one container (occasionally a few tightly-coupled ones that must share a fate). **Each pod gets its own IP address.** Pods are disposable: they are created and destroyed as workloads scale or move.
- **Node** — a **virtual machine that runs many pods**. It provides the CPU, memory, and the agent (kubelet) and container runtime that actually launch the pods scheduled onto it.
- **Cluster** — **all the nodes together, plus the control plane** (the API server, scheduler, and controllers) that decides what runs where and keeps the desired state. The control plane manages; the nodes do the work.
- **Service** — a **stable virtual address (and DNS name) that routes to the healthy pods** behind it. Because pods come and go (and change IPs), clients talk to the *service*, which load-balances across the current healthy pods. Services use a **separate internal IP range** (the cluster "service" range), distinct from pod IPs.

**Horizontal scaling.** To handle more load, Kubernetes adds **more pods** (the Horizontal Pod Autoscaler reacts to metrics like CPU). When the existing nodes run out of room to place new pods, the **cluster autoscaler adds more nodes**. Scaling out (more copies) is preferred over scaling up (bigger machines).

---

## 4. Azure CNI & IP-Consumption Math

How pods get their IPs determines how big the cluster's subnet must be.

**Traditional Azure CNI.** With the traditional Azure CNI networking mode, **every pod takes a real IP from the VNet subnet** — pods are first-class citizens on the VNet, directly addressable. To place pods quickly, **each node pre-reserves a block of subnet IPs up front**: by default room for about **30 pods per node, plus 1 IP for the node itself ≈ 31 IPs reserved per node** — whether or not those pods exist yet.

**The worked math.** Because each node reserves ~31 IPs:

| Nodes | Reserved IPs (≈ nodes × 31) |
|-------|-----------------------------|
| 3 | ≈ 93 |
| 10 | ≈ 310 |

On top of that, leave **surge / headroom** for rolling upgrades and scale-out (during an upgrade the cluster temporarily runs extra nodes). So a cluster that may grow to ~10 nodes needs well over 310 addresses once headroom is included.

This is why the **AKS subnet is sized `/22` (1,019 usable)** — comfortably above the ~310 + surge — while ordinary subnets that only host a handful of resources are `/24` or smaller.

**Azure CNI Overlay (alternative).** In CNI **Overlay** mode, **pod IPs come from a separate overlay address range**, not the VNet subnet. The VNet subnet then only needs enough IPs for the **nodes** (and a few platform endpoints), not every pod — making the subnet **far more IP-efficient** and removing the per-node block reservation as the sizing driver. It is the better choice when VNet address space is scarce or pod counts are very high.

---

## 5. How This Maps to Our Design

The platform's address plan applies the ideas above:

| Network | Size | Why |
|---------|------|-----|
| **VNet** | `/16` (65,531 usable) | A large private space to carve every subnet from, with ample room to add subnets and grow without re-addressing. |
| **AKS subnet** | `/22` (1,019 usable) | Sized for pod IP consumption under traditional Azure CNI (~31 IPs/node) plus upgrade/scale surge headroom — see the math above. |
| **Private endpoints subnet** | small (e.g. `/27`) | Each private endpoint consumes only one IP; a handful of managed-service endpoints needs very few addresses. |
| **Gateway / edge subnet** | small (e.g. `/27`) | The gateway/ingress front door needs only a small, dedicated slice. |

The principle is simple: **size each subnet to its real IP demand**. The cluster subnet is large because Azure CNI consumes an IP per pod; the supporting subnets are small because they hold only a few endpoints. Keeping them separate also lets each have its own NSG rules, so the cluster, the private endpoints, and the edge are each firewalled to exactly the traffic they should see.

---

This primer supports the **Networking** step of the [Infrastructure Guide](infrastructure-guide.md), where these concepts are turned into the actual VNet, subnets, and NSGs.
